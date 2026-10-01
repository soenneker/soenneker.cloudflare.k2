using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Soenneker.Cloudflare.K2.Models;

public sealed class K2Cors
{
    [JsonPropertyName("origins")]
    public required List<string> Origins { get; init; }
}
