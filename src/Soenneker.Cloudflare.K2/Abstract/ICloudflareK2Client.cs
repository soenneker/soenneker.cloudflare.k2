using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Soenneker.Cloudflare.K2.Models;

namespace Soenneker.Cloudflare.K2.Abstract;

/// <summary>Manages streams and sends/receives records using the account API and stream HTTP endpoints.</summary>
/// <remarks>Calls propagate failures and never automatically retry writes with uncertain outcomes. IDs are Cloudflare's 32-character hexadecimal IDs.</remarks>
public interface ICloudflareK2Client
{
    /// <summary>Creates a stream. HTTP input requires authentication by default.</summary>
    ValueTask<K2Stream> CreateStream(K2CreateStream settings, CancellationToken cancellationToken = default);
    /// <summary>Gets stream metadata without reading records.</summary>
    ValueTask<K2Stream> GetStream(string streamId, CancellationToken cancellationToken = default);
    /// <summary>Lists a page of streams, optionally filtering names by a case-insensitive substring.</summary>
    ValueTask<K2StreamPage> ListStreams(string? name = null, int page = 1, int perPage = 25, CancellationToken cancellationToken = default);
    /// <summary>Updates the supplied settings. Names cannot be changed.</summary>
    ValueTask<K2Stream> UpdateStream(string streamId, K2UpdateStream settings, CancellationToken cancellationToken = default);
    /// <summary>Deletes the stream and its retained records.</summary>
    ValueTask DeleteStream(string streamId, CancellationToken cancellationToken = default);
    /// <summary>Lists subscriptions with committed-position consumer lag via the account API.</summary>
    ValueTask<List<K2Subscription>> ListMonitoredSubscriptions(string streamId, CancellationToken cancellationToken = default);
    /// <summary>Atomically publishes a nonempty batch. A network failure can have an unknown outcome; blind retries can duplicate records.</summary>
    ValueTask SendMessages(string streamId, IReadOnlyList<K2Record> records, CancellationToken cancellationToken = default);
    /// <summary>Creates or returns a matching subscription. A conflicting start position causes an error. Earliest includes retained history.</summary>
    ValueTask<K2Subscription> CreateSubscription(string streamId, string name, bool startAtLatest = false, CancellationToken cancellationToken = default);
    /// <summary>Lists subscriptions, optionally matching a single name.</summary>
    ValueTask<List<K2Subscription>> ListSubscriptions(string streamId, string? name = null, CancellationToken cancellationToken = default);
    /// <summary>Gets a subscription's metadata.</summary>
    ValueTask<K2Subscription> GetSubscription(string streamId, string subscriptionId, CancellationToken cancellationToken = default);
    /// <summary>Deletes a subscription without deleting retained stream records.</summary>
    ValueTask DeleteSubscription(string streamId, string subscriptionId, CancellationToken cancellationToken = default);
    /// <summary>Leases up to maxRecords (1–10,000). Reusing a worker with an active lease returns that same batch. Empty reads have no lease.</summary>
    ValueTask<K2Batch> ReceiveMessages(string streamId, string subscriptionId, string workerId, int maxRecords = 1, CancellationToken cancellationToken = default);
    /// <summary>Acknowledges the entire batch. All handlers must succeed first; this does not delete the stream's records.</summary>
    ValueTask CompleteBatch(string streamId, string subscriptionId, string batchId, string workerId, CancellationToken cancellationToken = default);
    /// <summary>Releases the entire batch immediately for redelivery, including records already handled successfully.</summary>
    ValueTask AbandonBatch(string streamId, string subscriptionId, string batchId, string workerId, CancellationToken cancellationToken = default);
    /// <summary>Extends the owning worker's lease by five minutes and returns its Unix-millisecond expiration. Error 10218 means ownership is lost.</summary>
    ValueTask<long> RenewBatchLock(string streamId, string subscriptionId, string batchId, string workerId, CancellationToken cancellationToken = default);
}
