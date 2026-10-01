using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Soenneker.Cloudflare.K2.Models;

public sealed class K2HttpInput
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;
    [JsonPropertyName("authentication")]
    public bool Authentication { get; init; } = true;
    [JsonPropertyName("cors")]
    public K2Cors? Cors { get; init; }
}
