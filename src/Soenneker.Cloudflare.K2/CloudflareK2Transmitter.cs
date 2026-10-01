using System;
using System.Collections.Generic;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Soenneker.Cloudflare.K2.Abstract;
using Soenneker.Cloudflare.K2.Models;

namespace Soenneker.Cloudflare.K2;

public sealed class CloudflareK2Transmitter : ICloudflareK2Transmitter
{
    private readonly ICloudflareK2Client _client;

    public CloudflareK2Transmitter(ICloudflareK2Client client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    public ValueTask SendMessage<T>(string streamId, T message, string type, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CloudflareK2Client.ValidateId(streamId);
        return _client.SendMessages(streamId, [K2Record.FromJson(message, type, jsonTypeInfo)], cancellationToken);
    }

    public ValueTask SendMessages<T>(string streamId, IReadOnlyList<T> messages, string type, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CloudflareK2Client.ValidateId(streamId);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        if (messages.Count == 0)
            throw new ArgumentException("A batch must contain at least one message.", nameof(messages));
        var records = new K2Record[messages.Count];
        for (int i = 0; i < messages.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            records[i] = K2Record.FromJson(messages[i], type, jsonTypeInfo);
        }
        return _client.SendMessages(streamId, records, cancellationToken);
    }
}
