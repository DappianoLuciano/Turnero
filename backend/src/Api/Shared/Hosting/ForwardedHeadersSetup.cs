using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Api.Shared.Hosting;

/// <summary>
/// Real client IP behind a reverse proxy (Railway, Render, Nginx, Cloudflare…). Without this, every request arrives with
/// the proxy's IP and the per-IP rate limits of the public endpoints become one global limit for all businesses.
/// <para>Configuration (<c>ForwardedHeaders</c> section):</para>
/// <list type="bullet">
/// <item><c>Enabled</c>: off by default (local development talks to Kestrel directly).</item>
/// <item><c>KnownProxies</c> / <c>KnownNetworks</c>: comma separated IPs / CIDRs of the proxy. Prefer them when the
/// host publishes its proxy ranges.</item>
/// <item>With neither set, any proxy is trusted, but only for <c>ForwardLimit</c> hops (default 1: the right-most entry of
/// <c>X-Forwarded-For</c>, the one the platform's proxy wrote). Only safe when the API is reachable exclusively through
/// that proxy, which is the case on managed platforms.</item>
/// </list>
/// </summary>
internal static class ForwardedHeadersSetup
{
    public const string Section = "ForwardedHeaders";

    public static IServiceCollection AddAppForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(Section);
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = section.GetValue("ForwardLimit", 1);

            var proxies = Split(section["KnownProxies"]);
            var networks = Split(section["KnownNetworks"]);
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var proxy in proxies)
            {
                options.KnownProxies.Add(IPAddress.Parse(proxy));
            }

            foreach (var network in networks)
            {
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }
        });
        return services;
    }

    public static WebApplication UseAppForwardedHeaders(this WebApplication app)
    {
        if (app.Configuration.GetValue<bool>($"{Section}:Enabled"))
        {
            app.UseForwardedHeaders();
        }

        return app;
    }

    private static string[] Split(string? value) =>
        value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
}
