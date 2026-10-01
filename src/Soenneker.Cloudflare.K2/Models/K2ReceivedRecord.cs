using System.Text.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Soenneker.Cloudflare.K2.Models;

/// <summary>A consumed record, including its server-assigned timestamp.</summary>
public sealed class K2ReceivedRecord
{
    [JsonPropertyName("content")]
    public required byte[] Content { get; init; }
    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; init; }
    [JsonPropertyName("timestamp_ms")]
    public long TimestampMs { get; init; }
    public string GetContentAsString() => Encoding.UTF8.GetString(Content);
    public T? ReadJson<T>(JsonTypeInfo<T> jsonTypeInfo) => JsonSerializer.Deserialize(Content, jsonTypeInfo);
}
