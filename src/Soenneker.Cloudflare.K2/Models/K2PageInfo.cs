using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Soenneker.Cloudflare.K2.Models;

public sealed class K2PageInfo
{
    [JsonPropertyName("count")]
    public int Count { get; init; }
    [JsonPropertyName("page")]
    public int Page { get; init; }
    [JsonPropertyName("per_page")]
    public int PerPage { get; init; }
    [JsonPropertyName("total_count")]
    public int TotalCount { get; init; }
}
