using System.Text.Json.Serialization;
using System.Collections.Generic;
using Soenneker.Cloudflare.K2.Models;

namespace Soenneker.Cloudflare.K2.Serialization;

internal readonly record struct K2ProduceRequest(
    [property: JsonPropertyName("records")] IReadOnlyList<K2Record> Records);
