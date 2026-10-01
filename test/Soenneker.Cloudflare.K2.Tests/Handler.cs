using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Cloudflare.K2.Tests;

internal sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    public int Calls;
    public ConcurrentQueue<string> Paths { get; } = new();
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref Calls);
        Paths.Enqueue(request.RequestUri!.AbsolutePath);
        return send(request);
    }
}
