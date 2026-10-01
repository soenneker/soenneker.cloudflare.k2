using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Soenneker.Cloudflare.K2.Abstract;
using Soenneker.Cloudflare.K2.Options;

namespace Soenneker.Cloudflare.K2.Tests;

internal sealed class Receptor(ICloudflareK2Client client, CloudflareK2ReceptorOptions options,
    Func<string, string, CancellationToken, ValueTask> receive) : CloudflareK2Receptor(client, options, NullLogger.Instance)
{
    public override ValueTask OnMessageReceived(string messageContent, string type, CancellationToken cancellationToken = default)
        => receive(messageContent, type, cancellationToken);
}
