using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Cloudflare.K2.Tests;

internal sealed class DelayedReadStream : MemoryStream
{
    public bool Disposed { get; private set; }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return 0;
    }

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        base.Dispose(disposing);
    }
}
