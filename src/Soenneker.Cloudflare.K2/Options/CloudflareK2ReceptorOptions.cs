using System;

namespace Soenneker.Cloudflare.K2.Options;

/// <summary>Settings copied when a receptor is constructed. Each concurrent worker owns a distinct lease.</summary>
public sealed class CloudflareK2ReceptorOptions
{
    public required string StreamId { get; set; }
    public required string SubscriptionName { get; set; }
    public bool StartAtLatest { get; set; }
    public int MaxConcurrentCalls { get; set; } = 1;
    /// <summary>Defaults to one. With larger batches, a failure replays even records whose handlers already succeeded.</summary>
    public int MaxRecords { get; set; } = 1;
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan ErrorBackoff { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan LeaseRenewalInterval { get; set; } = TimeSpan.FromMinutes(1);
    /// <summary>Bounds automatic renewal and cancels slow handlers. Handlers must honor cancellation.</summary>
    public TimeSpan ProcessingTimeout { get; set; } = TimeSpan.FromMinutes(30);
}
