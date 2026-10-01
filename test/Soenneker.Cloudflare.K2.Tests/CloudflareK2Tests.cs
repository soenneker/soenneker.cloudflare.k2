using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Soenneker.Cloudflare.K2.Abstract;
using Soenneker.Cloudflare.K2.Exceptions;
using Soenneker.Cloudflare.K2.Models;
using Soenneker.Cloudflare.K2.Options;
using Soenneker.Cloudflare.K2.Registrars;

namespace Soenneker.Cloudflare.K2.Tests;

public sealed class CloudflareK2Tests
{
    private const string Stream = "11111111111111111111111111111111";
    private const string Subscription = "22222222222222222222222222222222";
    private const string Batch = "33333333333333333333333333333333";
    private const string Account = "44444444444444444444444444444444";

    [Test]
    public async Task Transmitter_preserves_json_and_type_header()
    {
        using var handler = new Handler(async request =>
        {
            Check(request.RequestUri!.AbsoluteUri == $"https://{Stream}.k2.cloudflarestorage.com/produce");
            Check(request.Headers.Authorization?.ToString() == "Bearer test-token");
            using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            JsonElement record = body.RootElement.GetProperty("records")[0];
            Check(Encoding.UTF8.GetString(record.GetProperty("content").GetBytesFromBase64()) == "\"hello\"");
            Check(record.GetProperty("headers").GetProperty("type").GetString() == "greeting");
            Check(!record.TryGetProperty("timestamp_ms", out _));
            return Response("{\"success\":true}");
        });
        using var http = new System.Net.Http.HttpClient(handler);
        await new CloudflareK2Transmitter(Client(http)).SendMessage(Stream, "hello", "greeting", TestJsonContext.Default.String);
        Check(handler.Calls == 1);
    }

    [Test]
    [Arguments(10212, false)]
    [Arguments(10211, true)]
    public async Task Produce_errors_are_exposed_without_hidden_resend(int code, bool retryable)
    {
        using var handler = new Handler(_ => Task.FromResult(Response(
            $$"""{"success":false,"error":{"code":{{code}},"message":"failed","retryable":{{retryable.ToString().ToLowerInvariant()}} } }""", HttpStatusCode.ServiceUnavailable)));
        using var http = new System.Net.Http.HttpClient(handler);
        CloudflareK2Exception error = await Throws<CloudflareK2Exception>(() => Client(http).SendMessages(Stream, [Record()]).AsTask());
        Check(error.ErrorCode == code && error.Retryable == retryable && handler.Calls == 1);
    }

    [Test]
    public async Task Receive_decodes_content_and_empty_batches()
    {
        int reads = 0;
        using var handler = new Handler(async request =>
        {
            using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Check(body.RootElement.GetProperty("worker_id").GetString() == "worker");
            Check(body.RootElement.GetProperty("max_records").GetInt32() == 1);
            return Response(++reads == 1 ? BatchResponse() : EmptyBatch);
        });
        using var http = new System.Net.Http.HttpClient(handler);
        K2Batch batch = await Client(http).ReceiveMessages(Stream, Subscription, "worker");
        Check(batch.BatchId == Batch && batch.Records.Single().GetContentAsString() == "hello");
        K2Batch empty = await Client(http).ReceiveMessages(Stream, Subscription, "worker");
        Check(empty.BatchId == null && empty.LeasedUntilMs == null && empty.Records.Count == 0);
    }

    [Test]
    public async Task Invalid_and_oversized_batches_do_not_send()
    {
        using var handler = new Handler(_ => throw new Exception("Unexpected network call"));
        using var http = new System.Net.Http.HttpClient(handler);
        CloudflareK2Client client = Client(http);
        await Throws<ArgumentException>(() => client.SendMessages(Stream, []).AsTask());
        await Throws<ArgumentException>(() => client.SendMessages(Stream, [new K2Record { Content = new byte[1_000_001] }]).AsTask());
        await Throws<ArgumentException>(() => client.SendMessages(Stream, Enumerable.Range(0, 4).Select(_ => new K2Record { Content = new byte[999_999] }).ToArray()).AsTask());
        await Throws<ArgumentException>(() => client.SendMessages("../another-host", [Record()]).AsTask());
        await Throws<ArgumentOutOfRangeException>(() => client.ReceiveMessages(Stream, Subscription, "worker", 10_001).AsTask());
        Check(handler.Calls == 0);
    }

    [Test]
    public async Task Management_uses_account_endpoint_and_authenticated_http_default()
    {
        using var handler = new Handler(async request =>
        {
            Check(request.RequestUri!.AbsoluteUri == $"https://api.cloudflare.com/client/v4/accounts/{Account}/k2/streams");
            using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Check(body.RootElement.GetProperty("http").GetProperty("authentication").GetBoolean());
            Check(!body.RootElement.TryGetProperty("retention_seconds", out _));
            return Response($$"""{"success":true,"result":{"id":"{{Stream}}","name":"orders","retention_seconds":604800} }""");
        });
        using var http = new System.Net.Http.HttpClient(handler);
        Check((await Client(http).CreateStream(new K2CreateStream { Name = "orders" })).Id == Stream);
    }

    [Test]
    public async Task Subscription_creation_preserves_start_position()
    {
        using var handler = new Handler(async request =>
        {
            Check(request.RequestUri!.AbsolutePath == "/subscriptions");
            using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Check(body.RootElement.GetProperty("start_at").GetProperty("type").GetString() == "latest");
            return Response($$"""{"success":true,"result":{"id":"{{Subscription}}"} }""");
        });
        using var http = new System.Net.Http.HttpClient(handler);
        Check((await Client(http).CreateSubscription(Stream, "orders-handler", true)).Id == Subscription);
    }

    [Test]
    public async Task Receptor_completes_only_after_handler_success()
    {
        TaskCompletionSource handled = Signal(); TaskCompletionSource release = Signal(); TaskCompletionSource ack = Signal();
        using Handler handler = ReceptorHandler(ack: () => ack.TrySetResult());
        using var http = new System.Net.Http.HttpClient(handler);
        await using var receptor = new Receptor(Client(http), Settings(), async (_, _, ct) =>
        {
            handled.TrySetResult(); await release.Task.WaitAsync(ct);
        });
        await receptor.Init();
        await handled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(!ack.Task.IsCompleted);
        release.TrySetResult();
        await ack.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await receptor.Stop();
        Check(handler.Paths.Count(p => p.EndsWith("/ack")) == 1 && !handler.Paths.Any(p => p.EndsWith("/nack")));
    }

    [Test]
    public async Task Receptor_failure_abandons_whole_batch()
    {
        TaskCompletionSource nack = Signal(); int handled = 0;
        using Handler handler = ReceptorHandler(nack: () => nack.TrySetResult(), recordCount: 2);
        using var http = new System.Net.Http.HttpClient(handler);
        CloudflareK2ReceptorOptions settings = Settings(); settings.MaxRecords = 2;
        await using var receptor = new Receptor(Client(http), settings, (_, _, _) =>
        {
            if (Interlocked.Increment(ref handled) == 2) throw new InvalidOperationException("handler failure");
            return ValueTask.CompletedTask;
        });
        await receptor.Init();
        await nack.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await receptor.Stop();
        Check(handled == 2 && !handler.Paths.Any(p => p.EndsWith("/ack")));
    }

    [Test]
    public async Task Receptor_renews_slow_handlers()
    {
        TaskCompletionSource renewed = Signal(); TaskCompletionSource ack = Signal();
        using Handler handler = ReceptorHandler(ack: () => ack.TrySetResult(), extend: () => renewed.TrySetResult());
        using var http = new System.Net.Http.HttpClient(handler);
        CloudflareK2ReceptorOptions settings = Settings(); settings.LeaseRenewalInterval = TimeSpan.FromMilliseconds(20);
        await using var receptor = new Receptor(Client(http), settings, async (_, _, ct) => await renewed.Task.WaitAsync(ct));
        await receptor.Init();
        await ack.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await receptor.Stop();
        Check(handler.Paths.Any(p => p.EndsWith("/extend")));
    }

    [Test]
    public async Task Lease_loss_cancels_handler_without_settling_stale_batch()
    {
        TaskCompletionSource cancelled = Signal();
        using Handler handler = ReceptorHandler(loseLease: true);
        using var http = new System.Net.Http.HttpClient(handler);
        CloudflareK2ReceptorOptions settings = Settings(); settings.LeaseRenewalInterval = TimeSpan.FromMilliseconds(20);
        await using var receptor = new Receptor(Client(http), settings, async (_, _, ct) =>
        {
            try { await Task.Delay(Timeout.Infinite, ct); }
            finally { cancelled.TrySetResult(); }
        });
        await receptor.Init();
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await receptor.Stop();
        Check(!handler.Paths.Any(p => p.EndsWith("/ack") || p.EndsWith("/nack")));
    }

    [Test]
    public async Task Shutdown_cancels_handler_and_releases_batch()
    {
        TaskCompletionSource started = Signal();
        using Handler handler = ReceptorHandler();
        using var http = new System.Net.Http.HttpClient(handler);
        await using var receptor = new Receptor(Client(http), Settings(), async (_, _, ct) =>
        {
            started.TrySetResult(); await Task.Delay(Timeout.Infinite, ct);
        });
        await receptor.Init();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await receptor.Stop().WaitAsync(TimeSpan.FromSeconds(5));
        Check(handler.Paths.Any(p => p.EndsWith("/nack")) && !handler.Paths.Any(p => p.EndsWith("/ack")));
    }

    [Test]
    public async Task Repeated_init_does_not_duplicate_workers()
    {
        using Handler handler = ReceptorHandler(empty: true);
        using var http = new System.Net.Http.HttpClient(handler);
        await using var receptor = new Receptor(Client(http), Settings(), (_, _, _) => ValueTask.CompletedTask);
        await Task.WhenAll(receptor.Init(), receptor.Init(), receptor.Init());
        await receptor.Stop();
        Check(handler.Paths.Count(p => p == "/subscriptions") == 1);
    }

    [Test]
    public async Task Concurrent_disposal_is_idempotent_and_prevents_restart()
    {
        using Handler handler = ReceptorHandler(empty: true);
        using var http = new System.Net.Http.HttpClient(handler);
        var receptor = new Receptor(Client(http), Settings(), (_, _, _) => ValueTask.CompletedTask);
        await receptor.Init();
        await Task.WhenAll(receptor.DisposeAsync().AsTask(), receptor.DisposeAsync().AsTask());
        await receptor.Stop();
        await Throws<ObjectDisposedException>(() => receptor.Init());
        Check(receptor.Completion.IsCompleted);
    }

    [Test]
    public void Registrar_resolves_configured_services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?> { ["Cloudflare:ApiKey"] = "test-token" }).Build());
        services.AddCloudflareK2AsSingleton(o => { o.AccountId = Account; o.ApiToken = "test-token"; });
        using ServiceProvider provider = services.BuildServiceProvider();
        Check(ReferenceEquals(provider.GetRequiredService<ICloudflareK2Client>(), provider.GetRequiredService<ICloudflareK2Client>()));
        Check(provider.GetRequiredService<ICloudflareK2Transmitter>() is CloudflareK2Transmitter);
    }

    [Test]
    public async Task Transient_receive_recovers_with_the_same_worker_id()
    {
        int reads = 0;
        string? worker = null;
        TaskCompletionSource recovered = Signal();
        using var handler = new Handler(async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/subscriptions")
                return Response($$"""{"success":true,"result":{"id":"{{Subscription}}"} }""");
            using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            string current = body.RootElement.GetProperty("worker_id").GetString()!;
            if (Interlocked.Increment(ref reads) == 1)
            {
                worker = current;
                throw new HttpRequestException("Lost consume response");
            }
            Check(current == worker);
            recovered.TrySetResult();
            return Response(EmptyBatch);
        });
        using var http = new System.Net.Http.HttpClient(handler);
        await using var receptor = new Receptor(Client(http), Settings(), (_, _, _) => ValueTask.CompletedTask);
        await receptor.Init();
        await recovered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await receptor.Stop();
        Check(reads >= 2);
    }

    [Test]
    public async Task Nonretryable_receive_fault_is_observable()
    {
        using var handler = new Handler(request => Task.FromResult(request.RequestUri!.AbsolutePath == "/subscriptions"
            ? Response($$"""{"success":true,"result":{"id":"{{Subscription}}"} }""")
            : Response("{\"success\":false,\"errors\":[{\"code\":10210,\"message\":\"permission denied\"}]}", HttpStatusCode.Forbidden)));
        using var http = new System.Net.Http.HttpClient(handler);
        var receptor = new Receptor(Client(http), Settings(), (_, _, _) => ValueTask.CompletedTask);
        await receptor.Init();
        CloudflareK2Exception error = await Throws<CloudflareK2Exception>(() => receptor.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        Check(error.ErrorCode == 10210 && !error.Retryable);
        await Throws<CloudflareK2Exception>(() => receptor.Stop());
        await receptor.DisposeAsync();
        Check(handler.Paths.Count(p => p.EndsWith("/consume")) == 1);
    }

    [Test]
    public async Task Expired_batch_is_neither_processed_nor_settled()
    {
        TaskCompletionSource consumed = Signal();
        using var handler = new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/subscriptions")
                return Task.FromResult(Response($$"""{"success":true,"result":{"id":"{{Subscription}}"} }"""));
            consumed.TrySetResult();
            return Task.FromResult(Response($$"""{"success":true,"result":{"batch_id":"{{Batch}}","leased_until_ms":1,"records":[{"content":"aGVsbG8=","headers":{"type":"test"} }] } }"""));
        });
        using var http = new System.Net.Http.HttpClient(handler);
        int calls = 0;
        await using var receptor = new Receptor(Client(http), Settings(), (_, _, _) => { Interlocked.Increment(ref calls); return ValueTask.CompletedTask; });
        await receptor.Init();
        await consumed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await receptor.Stop();
        Check(calls == 0 && !handler.Paths.Any(p => p.EndsWith("/ack") || p.EndsWith("/nack")));
    }

    [Test]
    public async Task Processing_timeout_cancels_and_abandons()
    {
        TaskCompletionSource released = Signal();
        using Handler handler = ReceptorHandler(nack: () => released.TrySetResult());
        using var http = new System.Net.Http.HttpClient(handler);
        CloudflareK2ReceptorOptions settings = Settings(); settings.ProcessingTimeout = TimeSpan.FromMilliseconds(30);
        await using var receptor = new Receptor(Client(http), settings, async (_, _, ct) => await Task.Delay(Timeout.Infinite, ct));
        await receptor.Init();
        await released.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await receptor.Stop();
        Check(!handler.Paths.Any(p => p.EndsWith("/ack")));
    }

    [Test]
    public async Task Concurrent_consumers_use_distinct_worker_ids()
    {
        var workers = new ConcurrentDictionary<string, byte>();
        TaskCompletionSource both = Signal();
        using var handler = new Handler(async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/subscriptions")
                return Response($$"""{"success":true,"result":{"id":"{{Subscription}}"} }""");
            using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            workers.TryAdd(body.RootElement.GetProperty("worker_id").GetString()!, 0);
            if (workers.Count == 2) both.TrySetResult();
            return Response(EmptyBatch);
        });
        using var http = new System.Net.Http.HttpClient(handler);
        CloudflareK2ReceptorOptions settings = Settings(); settings.MaxConcurrentCalls = 2;
        await using var receptor = new Receptor(Client(http), settings, (_, _, _) => ValueTask.CompletedTask);
        await receptor.Init();
        await both.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await receptor.Stop();
        Check(workers.Count == 2);
    }

    [Test]
    public async Task Stream_pagination_and_monitoring_preserve_metadata()
    {
        using var handler = new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/subscriptions"))
                return Task.FromResult(Response($$"""{"success":true,"result":[{"id":"{{Subscription}}","lag":{"status":"available","records":"12345678901234567890"} }] }"""));
            Check(request.RequestUri.Query.Contains("page=2&per_page=10&name=orders%20test"));
            return Task.FromResult(Response("{\"success\":true,\"result\":[],\"result_info\":{\"count\":0,\"page\":2,\"per_page\":10,\"total_count\":10}}"));
        });
        using var http = new System.Net.Http.HttpClient(handler);
        CloudflareK2Client client = Client(http);
        K2StreamPage page = await client.ListStreams("orders test", 2, 10);
        Check(page.ResultInfo.TotalCount == 10 && page.ResultInfo.Page == 2);
        Check((await client.ListMonitoredSubscriptions(Stream)).Single().Lag!.Records == "12345678901234567890");
    }

    [Test]
    public async Task Response_body_read_observes_http_timeout_and_disposes_stream()
    {
        var stream = new DelayedReadStream();
        using var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(stream)
        }));
        using var http = new System.Net.Http.HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(100) };
        await Throws<OperationCanceledException>(() => Client(http).ReceiveMessages(Stream, Subscription, "worker").AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5)));
        Check(stream.Disposed);
    }

    [Test]
    public async Task Oversized_batch_stops_before_encoding_remaining_records()
    {
        using var handler = new Handler(_ => throw new Exception("Unexpected network call"));
        using var http = new System.Net.Http.HttpClient(handler);
        var record = new K2Record { Content = new byte[999_999] };
        ArgumentException error = await Throws<ArgumentException>(() => Client(http)
            .SendMessages(Stream, [record, record, record, record, null!]).AsTask());
        Check(error is not ArgumentNullException && error.ParamName == "records" && handler.Calls == 0);
    }

    [Test]
    public async Task Cancelled_batch_is_rejected_before_validation_or_encoding()
    {
        using var handler = new Handler(_ => throw new Exception("Unexpected network call"));
        using var http = new System.Net.Http.HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Throws<OperationCanceledException>(() => Client(http).SendMessages(Stream, [null!], cancellation.Token).AsTask());
        await Throws<OperationCanceledException>(() => new CloudflareK2Transmitter(Client(http))
            .SendMessages(Stream, Array.Empty<string>(), "test", TestJsonContext.Default.String, cancellation.Token).AsTask());
        Check(handler.Calls == 0);
    }

    [Test]
    public async Task Invalid_receptor_settings_fail_before_starting_workers()
    {
        using var handler = new Handler(_ => throw new Exception("Unexpected network call"));
        using var http = new System.Net.Http.HttpClient(handler);
        CloudflareK2ReceptorOptions settings = Settings();
        settings.ProcessingTimeout = TimeSpan.MaxValue;
        await Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = new Receptor(Client(http), settings, (_, _, _) => ValueTask.CompletedTask);
            return Task.CompletedTask;
        });
        settings = Settings();
        settings.SubscriptionName = "invalid/name";
        await Throws<ArgumentException>(() =>
        {
            _ = new Receptor(Client(http), settings, (_, _, _) => ValueTask.CompletedTask);
            return Task.CompletedTask;
        });
        Check(handler.Calls == 0);
    }

    private static CloudflareK2Client Client(System.Net.Http.HttpClient http) => new(new TestCloudflareHttpClient(http),
        Microsoft.Extensions.Options.Options.Create(new CloudflareK2Options { AccountId = Account, ApiToken = "test-token" }));
    private static K2Record Record() => new() { Content = Encoding.UTF8.GetBytes("hello") };
    private static CloudflareK2ReceptorOptions Settings() => new()
    {
        StreamId = Stream, SubscriptionName = "test-handler", PollInterval = TimeSpan.FromMilliseconds(20), ErrorBackoff = TimeSpan.FromMilliseconds(100)
    };
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private const string EmptyBatch = "{\"success\":true,\"result\":{\"batch_id\":null,\"leased_until_ms\":null,\"records\":[]}}";
    private static string BatchResponse(int count = 1) => $$"""
        {"success":true,"result":{"batch_id":"{{Batch}}","leased_until_ms":{{DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds()}},"records":[{{string.Join(',', Enumerable.Repeat("{\"content\":\"aGVsbG8=\",\"headers\":{\"type\":\"greeting\"},\"timestamp_ms\":123}", count))}}]} }
        """;

    private static Handler ReceptorHandler(Action? ack = null, Action? nack = null, Action? extend = null, bool loseLease = false, int recordCount = 1, bool empty = false)
    {
        int consumes = 0;
        return new Handler(request =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path == "/subscriptions") return Task.FromResult(Response($$"""{"success":true,"result":{"id":"{{Subscription}}"} }"""));
            if (path.EndsWith("/consume")) return Task.FromResult(Response(!empty && Interlocked.Increment(ref consumes) == 1 ? BatchResponse(recordCount) : EmptyBatch));
            if (path.EndsWith("/extend"))
            {
                extend?.Invoke();
                return Task.FromResult(loseLease
                    ? Response("{\"success\":false,\"errors\":[{\"code\":10218,\"message\":\"lease lost\"}]}", HttpStatusCode.Conflict)
                    : Response($$"""{"success":true,"result":{"leased_until_ms":{{DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds()}} } }"""));
            }
            if (path.EndsWith("/ack")) ack?.Invoke();
            else if (path.EndsWith("/nack")) nack?.Invoke();
            else throw new Exception("Unexpected path " + path);
            return Task.FromResult(Response("{\"success\":true,\"result\":{}}"));
        });
    }

    private static HttpResponseMessage Response(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private static void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
    private static async Task<T> Throws<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T exception) { return exception; }
        throw new Exception("Expected " + typeof(T).Name);
    }
}
