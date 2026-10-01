using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Soenneker.Cloudflare.K2.Models;

public sealed class K2StartPosition
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }
}
