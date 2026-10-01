using Soenneker.Extensions.Task;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using Soenneker.Cloudflare.HttpClient.Abstract;
using Soenneker.Extensions.ValueTask;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Soenneker.Cloudflare.K2.Abstract;
using Soenneker.Cloudflare.K2.Exceptions;
using Soenneker.Cloudflare.K2.Models;
using Soenneker.Cloudflare.K2.Options;
using Soenneker.Cloudflare.K2.Serialization;

namespace Soenneker.Cloudflare.K2;

public sealed class CloudflareK2Client : ICloudflareK2Client
{
    private readonly ICloudflareHttpClient _http;
    private readonly string _token;
    private readonly string _accountId;
    private static K2JsonContext Json => K2JsonContext.Default;

    public CloudflareK2Client(ICloudflareHttpClient httpClient, IOptions<CloudflareK2Options> options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        _http = httpClient;
        _token = options.Value.ApiToken;
        _accountId = options.Value.AccountId;
    }

    public async ValueTask<K2Stream> CreateStream(K2CreateStream settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ValidateName(settings.Name, false);
        ValidateRetention(settings.RetentionSeconds);
        ArgumentNullException.ThrowIfNull(settings.Http);
        ValidateInputs(settings.Http, settings.WorkerBinding);
        using JsonDocument response = await Send(HttpMethod.Post, Streams(), Serialize(settings, Json.K2CreateStream),
            false, cancellationToken).NoSync();
        return Result(response, Json.K2Stream);
    }

    public async ValueTask<K2Stream> GetStream(string streamId, CancellationToken cancellationToken = default)
    {
        using JsonDocument response =
            await Send(HttpMethod.Get, Streams(streamId), null, false, cancellationToken).NoSync();
        return Result(response, Json.K2Stream);
    }

    public async ValueTask<K2StreamPage> ListStreams(string? name = null, int page = 1, int perPage = 25,
        CancellationToken cancellationToken = default)
    {
        if (page < 1)
            throw new ArgumentOutOfRangeException(nameof(page));
        if (perPage is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(perPage));
        string uri = $"{Streams()}?page={page}&per_page={perPage}";
        if (name != null)
            uri += "&name=" + Uri.EscapeDataString(name);
        using JsonDocument response = await Send(HttpMethod.Get, uri, null, false, cancellationToken).NoSync();
        return response.RootElement.Deserialize(Json.K2StreamPage) ?? throw new JsonException("Missing stream page.");
    }

    public async ValueTask<K2Stream> UpdateStream(string streamId, K2UpdateStream settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.RetentionSeconds == null && settings.Http == null && settings.WorkerBinding == null)
            throw new ArgumentException("Supply at least one stream setting.", nameof(settings));
        ValidateRetention(settings.RetentionSeconds);
        ValidateInputs(settings.Http, settings.WorkerBinding);
        using JsonDocument response = await Send(HttpMethod.Patch, Streams(streamId),
            Serialize(settings, Json.K2UpdateStream), false, cancellationToken).NoSync();
        return Result(response, Json.K2Stream);
    }

    public async ValueTask DeleteStream(string streamId, CancellationToken cancellationToken = default)
    {
        using JsonDocument response =
            await Send(HttpMethod.Delete, Streams(streamId), null, false, cancellationToken).NoSync();
    }

    public async ValueTask<List<K2Subscription>> ListMonitoredSubscriptions(string streamId,
        CancellationToken cancellationToken = default)
    {
        using JsonDocument response =
            await Send(HttpMethod.Get, Streams(streamId) + "/subscriptions", null, false, cancellationToken).NoSync();
        return Result(response, Json.ListK2Subscription);
    }

    public async ValueTask SendMessages(string streamId, IReadOnlyList<K2Record> records,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count == 0)
            throw new ArgumentException("A batch must contain at least one record.", nameof(records));
        foreach (K2Record record in records)
            ValidateRecord(record);
        byte[] body = Serialize(new K2ProduceRequest(records), Json.K2ProduceRequest);
        if (body.Length > 5_000_000)
            throw new ArgumentException("The encoded batch exceeds the 5 MB HTTP body limit.", nameof(records));
        using JsonDocument response =
            await Send(HttpMethod.Post, Endpoint(streamId) + "/produce", body, true, cancellationToken).NoSync();
    }

    public async ValueTask<K2Subscription> CreateSubscription(string streamId, string name, bool startAtLatest = false,
        CancellationToken cancellationToken = default)
    {
        ValidateName(name, true);
        var body = new K2CreateSubscription(name, new K2StartPosition { Type = startAtLatest ? "latest" : "earliest" });
        using JsonDocument response = await Send(HttpMethod.Post, Endpoint(streamId) + "/subscriptions",
            Serialize(body, Json.K2CreateSubscription), false, cancellationToken).NoSync();
        return Result(response, Json.K2Subscription);
    }

    public async ValueTask<List<K2Subscription>> ListSubscriptions(string streamId, string? name = null,
        CancellationToken cancellationToken = default)
    {
        string uri = Endpoint(streamId) + "/subscriptions";
        if (name != null)
            uri += "?name=" + Uri.EscapeDataString(name);
        using JsonDocument response = await Send(HttpMethod.Get, uri, null, false, cancellationToken).NoSync();
        return Result(response, Json.ListK2Subscription);
    }

    public async ValueTask<K2Subscription> GetSubscription(string streamId, string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        using JsonDocument response = await Send(HttpMethod.Get, Subscription(streamId, subscriptionId), null, false,
            cancellationToken).NoSync();
        return Result(response, Json.K2Subscription);
    }

    public async ValueTask DeleteSubscription(string streamId, string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        using JsonDocument response = await Send(HttpMethod.Delete, Subscription(streamId, subscriptionId), null, false,
            cancellationToken).NoSync();
    }

    public async ValueTask<K2Batch> ReceiveMessages(string streamId, string subscriptionId, string workerId,
        int maxRecords = 1, CancellationToken cancellationToken = default)
    {
        ValidateWorker(workerId);
        if (maxRecords is < 1 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(maxRecords));
        using JsonDocument response = await Send(HttpMethod.Post, Subscription(streamId, subscriptionId) + "/consume",
                Serialize(new K2ConsumeRequest(workerId, maxRecords), Json.K2ConsumeRequest), false, cancellationToken)
            .NoSync();
        return Result(response, Json.K2Batch);
    }

    public async ValueTask CompleteBatch(string streamId, string subscriptionId, string batchId, string workerId,
        CancellationToken cancellationToken = default)
    {
        using JsonDocument response =
            await Settle(streamId, subscriptionId, batchId, workerId, "ack", cancellationToken).NoSync();
    }

    public async ValueTask AbandonBatch(string streamId, string subscriptionId, string batchId, string workerId,
        CancellationToken cancellationToken = default)
    {
        using JsonDocument response =
            await Settle(streamId, subscriptionId, batchId, workerId, "nack", cancellationToken).NoSync();
    }

    public async ValueTask<long> RenewBatchLock(string streamId, string subscriptionId, string batchId, string workerId,
        CancellationToken cancellationToken = default)
    {
        using JsonDocument response =
            await Settle(streamId, subscriptionId, batchId, workerId, "extend", cancellationToken).NoSync();
        return Result(response, Json.K2Lease).LeasedUntilMs;
    }

    private Task<JsonDocument> Settle(string stream, string subscription, string batch, string worker, string action,
        CancellationToken ct)
    {
        ValidateId(batch);
        ValidateWorker(worker);
        return Send(HttpMethod.Post, $"{Subscription(stream, subscription)}/batches/{batch}/{action}",
            Serialize(new K2WorkerRequest(worker), Json.K2WorkerRequest), false, ct);
    }

    private async Task<JsonDocument> Send(HttpMethod method, string uri, byte[]? body, bool produce,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, uri);
        if (body != null)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        System.Net.Http.HttpClient http = string.IsNullOrWhiteSpace(_token)
            ? await _http.Get(ct).NoSync()
            : await _http.Get(_token, ct).NoSync();
        using HttpResponseMessage response = await http.SendAsync(request, ct).NoSync();
        // K2 limits consume responses to 10 MB; cancellation also applies to reading the body.
        Stream content = await response.Content.ReadAsStreamAsync(ct).NoSync();
        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(content, cancellationToken: ct).NoSync();
        }
        catch (JsonException) when (!response.IsSuccessStatusCode)
        {
            throw new CloudflareK2Exception($"K2 returned HTTP {(int)response.StatusCode} without a JSON error.",
                response.StatusCode, null, false);
        }

        JsonElement root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object && response.IsSuccessStatusCode &&
            root.TryGetProperty("success", out JsonElement success) && success.ValueKind == JsonValueKind.True)
            return document;

        int? code = null;
        bool retryable = false;
        string message = $"K2 request failed (HTTP {(int)response.StatusCode}).";
        if (root.ValueKind == JsonValueKind.Object)
        {
            JsonElement error = default;
            if (produce)
                root.TryGetProperty("error", out error);
            else if (root.TryGetProperty("errors", out JsonElement errors) && errors.ValueKind == JsonValueKind.Array &&
                     errors.GetArrayLength() > 0)
                error = errors[0];
            if (error.ValueKind == JsonValueKind.Object)
            {
                if (error.TryGetProperty("code", out JsonElement c) && c.ValueKind == JsonValueKind.Number &&
                    c.TryGetInt32(out int value))
                    code = value;
                if (error.TryGetProperty("message", out JsonElement m) && m.ValueKind == JsonValueKind.String)
                    message = m.GetString()!;
                retryable = produce
                    ? error.TryGetProperty("retryable", out JsonElement r) && r.ValueKind == JsonValueKind.True
                    : code is 10211 or 10214 or 10216 or 10217;
            }
        }

        document.Dispose();
        throw new CloudflareK2Exception(message, response.StatusCode, code, retryable);
    }

    private static byte[] Serialize<T>(T value, JsonTypeInfo<T> info) =>
        JsonSerializer.SerializeToUtf8Bytes(value, info);

    private static T Result<T>(JsonDocument document, JsonTypeInfo<T> info) =>
        document.RootElement.GetProperty("result").Deserialize(info) ??
        throw new JsonException("K2 returned a null result.");

    private string Streams(string? streamId = null)
    {
        ValidateId(_accountId);
        string uri = $"https://api.cloudflare.com/client/v4/accounts/{_accountId}/k2/streams";
        if (streamId == null)
            return uri;
        ValidateId(streamId);
        return uri + "/" + streamId;
    }

    private static string Endpoint(string stream)
    {
        ValidateId(stream);
        return $"https://{stream}.k2.cloudflarestorage.com";
    }

    private static string Subscription(string stream, string subscription)
    {
        ValidateId(subscription);
        return Endpoint(stream) + "/subscriptions/" + subscription;
    }

    internal static void ValidateId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (id.Length != 32)
            throw new ArgumentException("Expected a 32-character hexadecimal Cloudflare ID.", nameof(id));
        foreach (char c in id)
            if (!char.IsAsciiHexDigit(c))
                throw new ArgumentException("Expected a hexadecimal Cloudflare ID.", nameof(id));
    }

    private static void ValidateWorker(string worker)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worker);
        if (worker.Length > 256)
            throw new ArgumentException("Worker IDs cannot exceed 256 characters.", nameof(worker));
    }

    private static void ValidateName(string name, bool allowHyphen)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > 128)
            throw new ArgumentException("Names cannot exceed 128 characters.", nameof(name));
        foreach (char c in name)
            if (!char.IsAsciiLetterOrDigit(c) && c != '_' && !(allowHyphen && c == '-'))
                throw new ArgumentException("Name contains unsupported characters.", nameof(name));
    }

    private static void ValidateRetention(int? seconds)
    {
        if (seconds is < 3600 or > 2_592_000)
            throw new ArgumentOutOfRangeException(nameof(seconds));
    }

    private static void ValidateInputs(K2HttpInput? http, K2WorkerBindingInput? binding)
    {
        if (http is { Enabled: false } && binding is { Enabled: false })
            throw new ArgumentException("At least one input must be enabled.");
        if (http?.Cors is { } cors && (cors.Origins == null || cors.Origins.Count > 5))
            throw new ArgumentException("At most five CORS origins are supported.");
    }

    private static void ValidateRecord(K2Record record)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(record.Content);
        long headersSize = 0;
        if (record.Headers is { } headers)
        {
            if (headers.Count > 32)
                throw new ArgumentException("A record may have at most 32 headers.");
            foreach ((string key, string value) in headers)
            {
                ArgumentNullException.ThrowIfNull(value);
                int keySize = Encoding.UTF8.GetByteCount(key);
                int valueSize = Encoding.UTF8.GetByteCount(value);
                if (keySize > 256 || valueSize > 8192)
                    throw new ArgumentException("A record header exceeds its UTF-8 byte limit.");
                headersSize += keySize + valueSize;
            }
        }

        if (headersSize > 65_536 || record.Content.LongLength + headersSize > 1_000_000)
            throw new ArgumentException("A record exceeds K2's content or header size limit.");
    }
}