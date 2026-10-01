using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Soenneker.Cloudflare.K2.Models;

/// <summary>A K2 stream's configuration; retrieving it does not consume records.</summary>
public sealed class K2Stream
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }
    [JsonPropertyName("name")]
    public required string Name { get; init; }
    [JsonPropertyName("endpoint")]
    public string? Endpoint { get; init; }
    [JsonPropertyName("retention_seconds")]
    public int RetentionSeconds { get; init; }
    [JsonPropertyName("http")]
    public K2HttpInput? Http { get; init; }
    [JsonPropertyName("worker_binding")]
    public K2WorkerBindingInput? WorkerBinding { get; init; }
    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("modified_at")]
    public DateTimeOffset ModifiedAt { get; init; }
}
