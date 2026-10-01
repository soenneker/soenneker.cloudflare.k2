using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Soenneker.Cloudflare.K2.Models;

/// <summary>A single page of streams. Use ResultInfo to request subsequent pages.</summary>
public sealed class K2StreamPage
{
    [JsonPropertyName("result")]
    public required List<K2Stream> Result { get; init; }
    [JsonPropertyName("result_info")]
    public required K2PageInfo ResultInfo { get; init; }
}
