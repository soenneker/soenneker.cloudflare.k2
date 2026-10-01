using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Soenneker.Cloudflare.K2.Models;

/// <summary>Lag from committed positions. Records is a decimal string and is null when measurement is unavailable.</summary>
public sealed class K2ConsumerLag
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    [JsonPropertyName("records")]
    public string? Records { get; init; }
}
