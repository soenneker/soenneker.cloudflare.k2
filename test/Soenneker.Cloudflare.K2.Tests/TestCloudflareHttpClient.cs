using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Soenneker.Cloudflare.HttpClient.Abstract;

namespace Soenneker.Cloudflare.K2.Tests;

internal sealed class TestCloudflareHttpClient(System.Net.Http.HttpClient http) : ICloudflareHttpClient
{
    public ValueTask<System.Net.Http.HttpClient> Get(CancellationToken cancellationToken = default) => Get("default-token", cancellationToken);

    public ValueTask<System.Net.Http.HttpClient> Get(string apiKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return ValueTask.FromResult(http);
    }

    public ValueTask<bool> Remove(CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
    public ValueTask<bool> Remove(string apiKey, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
    public bool RemoveSync(CancellationToken cancellationToken = default) => false;
    public bool RemoveSync(string apiKey, CancellationToken cancellationToken = default) => false;
    public void Dispose() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
