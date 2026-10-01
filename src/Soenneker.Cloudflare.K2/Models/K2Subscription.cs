using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Soenneker.Cloudflare.K2.Models;

public sealed class K2Subscription
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }
    [JsonPropertyName("name")]
    public string? Name { get; init; }
    [JsonPropertyName("start_at")]
    public K2StartPosition? StartAt { get; init; }
    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; init; }
    [JsonPropertyName("modified_at")]
    public DateTimeOffset? ModifiedAt { get; init; }
    [JsonPropertyName("lag")]
    public K2ConsumerLag? Lag { get; init; }
}
