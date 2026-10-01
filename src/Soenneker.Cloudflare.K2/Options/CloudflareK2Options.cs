namespace Soenneker.Cloudflare.K2.Options;

/// <summary>Credentials for K2. The token needs the appropriate K2 Config, Produce, and Consume permissions.</summary>
public sealed class CloudflareK2Options
{
    public string AccountId { get; set; } = "";
    /// <summary>Optional token override. When empty, uses the shared provider's Cloudflare:ApiKey.</summary>
    public string ApiToken { get; set; } = "";
}
