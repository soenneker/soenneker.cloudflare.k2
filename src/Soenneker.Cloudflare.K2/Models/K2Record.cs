using System.Text.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Soenneker.Cloudflare.K2.Models;

/// <summary>A record's binary content and optional string headers. HTTP serialization encodes content as standard base64.</summary>
public sealed class K2Record
{
    [JsonPropertyName("content")]
    public required byte[] Content { get; init; }
    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; init; }

    public static K2Record FromJson<T>(T value, string type, JsonTypeInfo<T> jsonTypeInfo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        return new K2Record
        {
            Content = JsonSerializer.SerializeToUtf8Bytes(value, jsonTypeInfo),
            Headers = new() { ["type"] = type, ["content-type"] = "application/json" }
        };
    }
}
