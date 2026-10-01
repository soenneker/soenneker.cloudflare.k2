using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Soenneker.Cloudflare.K2.Models;

/// <summary>Patch settings. Null fields are omitted; stream names cannot be changed.</summary>
public sealed class K2UpdateStream
{
    [JsonPropertyName("retention_seconds")]
    public int? RetentionSeconds { get; init; }
    [JsonPropertyName("http")]
    public K2HttpInput? Http { get; init; }
    [JsonPropertyName("worker_binding")]
    public K2WorkerBindingInput? WorkerBinding { get; init; }
}
