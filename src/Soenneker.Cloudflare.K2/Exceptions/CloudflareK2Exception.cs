using System.Net;
using System.Net.Http;

namespace Soenneker.Cloudflare.K2.Exceptions;

/// <summary>A rejected K2 request. Retryable reflects the documented K2 error, not merely the HTTP status.</summary>
public sealed class CloudflareK2Exception(string message, HttpStatusCode statusCode, int? errorCode, bool retryable)
    : HttpRequestException(message, null, statusCode)
{
    public int? ErrorCode { get; } = errorCode;
    public bool Retryable { get; } = retryable;
}
