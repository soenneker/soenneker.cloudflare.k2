using System;
using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Cloudflare.K2.Abstract;

/// <summary>A subscription processor following the Service Bus Init/OnMessageReceived pattern.</summary>
/// <remarks>Delivery is at least once. Handlers must be idempotent and honor cancellation. No native dead-letter or duplicate-detection behavior is implied.</remarks>
public interface ICloudflareK2Receptor : IAsyncDisposable
{
    /// <summary>Creates or reuses the configured subscription on an existing stream and starts workers. Repeated calls while running do nothing.</summary>
    Task Init(CancellationToken cancellationToken = default);
    /// <summary>Cancels workers and waits for handlers and lease cleanup. A cancelled wait does not undo the stop request.</summary>
    Task Stop(CancellationToken cancellationToken = default);
    /// <summary>Observes the current run. Nonretryable receive errors fault this task and cancel sibling workers.</summary>
    Task Completion { get; }
    /// <summary>Handles JSON/text content and its type header. Success permits completion only after all records in the batch succeed.</summary>
    ValueTask OnMessageReceived(string messageContent, string type, CancellationToken cancellationToken = default);
}
