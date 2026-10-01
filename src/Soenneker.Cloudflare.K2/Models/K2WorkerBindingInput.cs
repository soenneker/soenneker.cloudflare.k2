using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Soenneker.Cloudflare.K2.Models;

public sealed class K2WorkerBindingInput
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;
}
