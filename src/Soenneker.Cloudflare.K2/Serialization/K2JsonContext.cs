using System.Collections.Generic;
using System.Text.Json.Serialization;
using Soenneker.Cloudflare.K2.Models;

namespace Soenneker.Cloudflare.K2.Serialization;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(K2Record))]
[JsonSerializable(typeof(K2ConsumeRequest))]
[JsonSerializable(typeof(K2WorkerRequest))]
[JsonSerializable(typeof(K2CreateSubscription))]
[JsonSerializable(typeof(K2CreateStream))]
[JsonSerializable(typeof(K2UpdateStream))]
[JsonSerializable(typeof(K2Stream))]
[JsonSerializable(typeof(K2StreamPage))]
[JsonSerializable(typeof(K2Subscription))]
[JsonSerializable(typeof(List<K2Subscription>))]
[JsonSerializable(typeof(K2Batch))]
[JsonSerializable(typeof(K2Lease))]
internal partial class K2JsonContext : JsonSerializerContext;
