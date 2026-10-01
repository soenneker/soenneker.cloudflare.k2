using Soenneker.Extensions.ValueTask;
using Soenneker.Extensions.Task;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Soenneker.Asyncs.Locks;
using Soenneker.Atomics.ValueBools;
using Soenneker.Cloudflare.K2.Abstract;
using Soenneker.Cloudflare.K2.Exceptions;
using Soenneker.Cloudflare.K2.Models;
using Soenneker.Cloudflare.K2.Options;

namespace Soenneker.Cloudflare.K2;

public abstract class CloudflareK2Receptor : ICloudflareK2Receptor
{
    private readonly ICloudflareK2Client _client;
    private readonly CloudflareK2ReceptorOptions _options;
    private readonly AsyncLock _lifecycle = new();
    private CancellationTokenSource? _run;
    private ValueAtomicBool _disposed;
    protected ILogger Logger { get; }
    public Task Completion { get; private set; } = Task.CompletedTask;

    protected CloudflareK2Receptor(ICloudflareK2Client client, CloudflareK2ReceptorOptions options, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        CloudflareK2Client.ValidateId(options.StreamId);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.SubscriptionName);
        if (options.MaxConcurrentCalls is < 1 or > 128 || options.MaxRecords is < 1 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(options));
        if (options.PollInterval <= TimeSpan.Zero || options.ErrorBackoff <= TimeSpan.Zero || options.ProcessingTimeout <= TimeSpan.Zero ||
            options.LeaseRenewalInterval <= TimeSpan.Zero || options.LeaseRenewalInterval >= TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(options));
        _client = client;
        Logger = logger;
        _options = new CloudflareK2ReceptorOptions
        {
            StreamId = options.StreamId, SubscriptionName = options.SubscriptionName, StartAtLatest = options.StartAtLatest,
            MaxConcurrentCalls = options.MaxConcurrentCalls, MaxRecords = options.MaxRecords, PollInterval = options.PollInterval,
            ErrorBackoff = options.ErrorBackoff, ProcessingTimeout = options.ProcessingTimeout, LeaseRenewalInterval = options.LeaseRenewalInterval
        };
    }

    public async Task Init(CancellationToken cancellationToken = default)
    {
        using var lease = await _lifecycle.Lock(cancellationToken).NoSync();
        ObjectDisposedException.ThrowIf(_disposed.Read(), this);
        if (_run != null && !Completion.IsCompleted) return;
        K2Subscription subscription = await _client.CreateSubscription(_options.StreamId, _options.SubscriptionName,
            _options.StartAtLatest, cancellationToken).NoSync();
        CloudflareK2Client.ValidateId(subscription.Id);
        _run?.Dispose();
        _run = new CancellationTokenSource();
        CancellationTokenSource run = _run;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workers = new Task[_options.MaxConcurrentCalls];
        for (int i = 0; i < workers.Length; i++)
            workers[i] = RunWorker(subscription.Id, Guid.NewGuid().ToString("N"), run, started.Task);

        Completion = Task.WhenAll(workers);
        // Publish the run before allowing any worker to process, even if HTTP completes synchronously.
        started.SetResult();
    }

    public async Task Stop(CancellationToken cancellationToken = default)
    {
        using var lease = await _lifecycle.Lock(cancellationToken).NoSync();
        if (_run == null) return;
        await _run.CancelAsync().NoSync();
        try { await Completion.WaitAsync(cancellationToken).NoSync(); }
        finally
        {
            if (Completion.IsCompleted)
            {
                _run.Dispose();
                _run = null;
            }
        }
    }

    public abstract ValueTask OnMessageReceived(string messageContent, string type, CancellationToken cancellationToken = default);

    private async Task RunWorker(string subscription, string worker, CancellationTokenSource run, Task started)
    {
        await started.NoSync();
        CancellationToken ct = run.Token;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    K2Batch batch = await _client.ReceiveMessages(_options.StreamId, subscription, worker, _options.MaxRecords, ct).NoSync();
                    if (batch.Records.Count == 0)
                    {
                        await Task.Delay(_options.PollInterval, ct).NoSync();
                        continue;
                    }
                    if (batch.BatchId == null || batch.LeasedUntilMs == null)
                        throw new InvalidOperationException("K2 returned records without a batch lease.");
                    await ProcessBatch(subscription, worker, batch, ct).NoSync();
                }
                catch (CloudflareK2Exception ex) when (ex.Retryable)
                {
                    Logger.LogWarning(ex, "K2 receive is temporarily unavailable for subscription {Subscription}", subscription);
                    await Task.Delay(_options.ErrorBackoff, ct).NoSync();
                }
                catch (HttpRequestException ex) when (ex is not CloudflareK2Exception)
                {
                    // The same worker ID recovers an existing lease after a lost consume/ack response.
                    Logger.LogWarning(ex, "K2 transport failed for subscription {Subscription}; recovering with the same worker", subscription);
                    await Task.Delay(_options.ErrorBackoff, ct).NoSync();
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    await Task.Delay(_options.ErrorBackoff, ct).NoSync();
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Logger.LogError(ex, "K2 receptor stopped for subscription {Subscription}", subscription);
            await run.CancelAsync().NoSync();
            throw;
        }
    }

    private async Task ProcessBatch(string subscription, string worker, K2Batch batch, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_options.ProcessingTimeout);
        using var processing = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        using var renewalStop = CancellationTokenSource.CreateLinkedTokenSource(processing.Token);
        long leasedUntil = batch.LeasedUntilMs!.Value;
        SetLeaseDeadline();
        Task renewal = Renew();
        Exception? failure = null;
        try
        {
            foreach (K2ReceivedRecord record in batch.Records)
            {
                processing.Token.ThrowIfCancellationRequested();
                if (record.Headers == null || !record.Headers.TryGetValue("type", out string? type) || string.IsNullOrWhiteSpace(type))
                    throw new InvalidOperationException("K2 record is missing its type header.");
                await OnMessageReceived(record.GetContentAsString(), type, processing.Token).NoSync();
            }
            processing.Token.ThrowIfCancellationRequested();
        }
        catch (Exception ex) { failure = ex; }
        finally
        {
            await renewalStop.CancelAsync().NoSync();
            try { await renewal.NoSync(); }
            catch (Exception ex) { failure = ex; }
        }

        if (failure == null && !processing.IsCancellationRequested && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < leasedUntil)
        {
            // Do not abandon on an ambiguous acknowledgement failure; receiving again with this worker recovers its lease.
            await _client.CompleteBatch(_options.StreamId, subscription, batch.BatchId!, worker, ct).NoSync();
            return;
        }

        Logger.LogWarning(failure, "K2 batch {Batch} was not completed; records may be delivered again", batch.BatchId);
        // A stale owner must not release a batch now owned by another consumer.
        if (failure is not CloudflareK2Exception { ErrorCode: 10218 } && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < leasedUntil)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try { await _client.AbandonBatch(_options.StreamId, subscription, batch.BatchId!, worker, cleanup.Token).NoSync(); }
            catch (Exception ex) { Logger.LogWarning(ex, "K2 batch release failed; the lease will expire"); }
        }
        ct.ThrowIfCancellationRequested();
        await Task.Delay(_options.ErrorBackoff, ct).NoSync();

        async Task Renew()
        {
            using var timer = new PeriodicTimer(_options.LeaseRenewalInterval);
            try
            {
                while (await timer.WaitForNextTickAsync(renewalStop.Token).NoSync())
                {
                    leasedUntil = await _client.RenewBatchLock(_options.StreamId, subscription, batch.BatchId!, worker, renewalStop.Token).NoSync();
                    SetLeaseDeadline();
                }
            }
            catch (OperationCanceledException) when (renewalStop.IsCancellationRequested) { }
            catch
            {
                await processing.CancelAsync().NoSync();
                throw;
            }
        }

        void SetLeaseDeadline()
        {
            long remaining = leasedUntil - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (remaining <= 0) processing.Cancel();
            else processing.CancelAfter(TimeSpan.FromMilliseconds(Math.Min(remaining, uint.MaxValue - 1L)));
        }
    }

    public async ValueTask DisposeAsync()
    {
        using var lease = await _lifecycle.Lock().NoSync();
        if (!_disposed.TrySetTrue()) return;
        if (_run == null) return;
        try
        {
            await _run.CancelAsync().NoSync();
            await Completion.NoSync();
        }
        finally { _run.Dispose(); _run = null; }
    }
}
