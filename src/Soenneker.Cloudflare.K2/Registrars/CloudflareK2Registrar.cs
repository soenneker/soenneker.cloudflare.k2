using System;
using Soenneker.Cloudflare.HttpClient.Registrars;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Soenneker.Cloudflare.K2.Abstract;
using Soenneker.Cloudflare.K2.Options;

namespace Soenneker.Cloudflare.K2.Registrars;

/// <summary>Registers K2 clients and typed transmitters. Register each application-specific receptor separately.</summary>
public static class CloudflareK2Registrar
{
    /// <summary>Registers singleton services and binds Cloudflare:K2 configuration, with optional programmatic overrides.</summary>
    public static IServiceCollection AddCloudflareK2AsSingleton(this IServiceCollection services, Action<CloudflareK2Options>? configure = null)
    {
        Configure(services, configure);
        services.AddCloudflareHttpClientAsSingleton();
        services.TryAddSingleton<ICloudflareK2Client, CloudflareK2Client>();
        services.TryAddSingleton<ICloudflareK2Transmitter, CloudflareK2Transmitter>();
        return services;
    }

    /// <summary>Registers scoped services and binds Cloudflare:K2 configuration, with optional programmatic overrides.</summary>
    public static IServiceCollection AddCloudflareK2AsScoped(this IServiceCollection services, Action<CloudflareK2Options>? configure = null)
    {
        Configure(services, configure);
        services.AddCloudflareHttpClientAsSingleton();
        services.TryAddScoped<ICloudflareK2Client, CloudflareK2Client>();
        services.TryAddScoped<ICloudflareK2Transmitter, CloudflareK2Transmitter>();
        return services;
    }

    private static void Configure(IServiceCollection services, Action<CloudflareK2Options>? configure)
    {
        services.AddOptions<CloudflareK2Options>().BindConfiguration("Cloudflare:K2");
        if (configure != null) services.Configure(configure);
    }
}
