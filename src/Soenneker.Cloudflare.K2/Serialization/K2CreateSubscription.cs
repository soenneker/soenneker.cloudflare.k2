using System.Text.Json.Serialization;
using System.Collections.Generic;
using Soenneker.Cloudflare.K2.Models;

namespace Soenneker.Cloudflare.K2.Serialization;

internal readonly record struct K2CreateSubscription(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("start_at")] K2StartPosition StartAt);
