using System.Text.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Soenneker.Cloudflare.K2.Models;

/// <summary>A lease over a whole batch. An empty read has no batch ID or lease expiration.</summary>
public sealed class K2Batch
{
    [JsonPropertyName("batch_id")]
    public string? BatchId { get; init; }
    [JsonPropertyName("leased_until_ms")]
    public long? LeasedUntilMs { get; init; }
    [JsonPropertyName("records")]
    public required List<K2ReceivedRecord> Records { get; init; }
}
