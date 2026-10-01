using System.Collections.Generic;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Cloudflare.K2.Abstract;

/// <summary>Publishes typed JSON messages with the same type header used by Service Bus receptors.</summary>
public interface ICloudflareK2Transmitter
{
    /// <summary>Serializes and sends one message. Completion means K2 accepted the record. Failures propagate to the caller.</summary>
    ValueTask SendMessage<T>(string streamId, T message, string type, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default);
    /// <summary>Serializes and atomically sends one batch. Oversized batches are rejected rather than split into partial sends.</summary>
    ValueTask SendMessages<T>(string streamId, IReadOnlyList<T> messages, string type, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default);
}
