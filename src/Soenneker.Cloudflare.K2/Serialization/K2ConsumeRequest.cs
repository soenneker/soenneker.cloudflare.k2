using System.Text.Json.Serialization;
using System.Collections.Generic;
using Soenneker.Cloudflare.K2.Models;

namespace Soenneker.Cloudflare.K2.Serialization;

internal readonly record struct K2ConsumeRequest(
    [property: JsonPropertyName("worker_id")] string WorkerId,
    [property: JsonPropertyName("max_records")] int MaxRecords);
