using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Soenneker.Cloudflare.K2.Models;

/// <summary>Creation settings. HTTP publishing is authenticated by default.</summary>
public sealed class K2CreateStream
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }
    [JsonPropertyName("retention_seconds")]
    public int? RetentionSeconds { get; init; }
    [JsonPropertyName("http")]
    public K2HttpInput Http { get; init; } = new();
    [JsonPropertyName("worker_binding")]
    public K2WorkerBindingInput? WorkerBinding { get; init; }
}
