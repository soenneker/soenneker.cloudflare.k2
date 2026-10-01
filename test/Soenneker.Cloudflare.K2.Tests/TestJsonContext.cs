using System.Text.Json.Serialization;

namespace Soenneker.Cloudflare.K2.Tests;

[JsonSerializable(typeof(string))]
internal partial class TestJsonContext : JsonSerializerContext;
