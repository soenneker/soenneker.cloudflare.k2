using System;
using System.Collections.Generic;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Soenneker.Cloudflare.K2.Abstract;
using Soenneker.Cloudflare.K2.Models;

namespace Soenneker.Cloudflare.K2;

public sealed class CloudflareK2Transmitter(ICloudflareK2Client client) : ICloudflareK2Transmitter
{
    public ValueTask SendMessage<T>(string streamId, T message, string type, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return client.SendMessages(streamId, [K2Record.FromJson(message, type, jsonTypeInfo)], cancellationToken);
    }

    public ValueTask SendMessages<T>(string streamId, IReadOnlyList<T> messages, string type, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var records = new K2Record[messages.Count];
        for (int i = 0; i < messages.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            records[i] = K2Record.FromJson(messages[i], type, jsonTypeInfo);
        }
        return client.SendMessages(streamId, records, cancellationToken);
    }
}
