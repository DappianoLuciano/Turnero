using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Api.Shared.Tenancy;

/// <summary>
/// Rate limit of the public (anonymous) booking endpoints, per client IP, so nobody can flood a business's agenda
/// or enumerate it. Limits are configurable (<c>RateLimiting:PublicPerMinute</c>, <c>RateLimiting:PublicBookingsPerHour</c>).
/// </summary>
internal static class PublicRateLimiting
{
    public const string Policy = "public";
    public const string BookingPolicy = "public-booking";

    private const int DefaultPerMinute = 300;
    private const int DefaultBookingsPerHour = 60;

    public static IServiceCollection AddPublicRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(Policy, httpContext =>
            {
                var limit = httpContext.RequestServices.GetRequiredService<IConfiguration>()
                    .GetValue("RateLimiting:PublicPerMinute", DefaultPerMinute);
                return RateLimitPartition.GetFixedWindowLimiter(ClientKey(httpContext), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                });
            });
            options.AddPolicy(BookingPolicy, httpContext =>
            {
                var limit = httpContext.RequestServices.GetRequiredService<IConfiguration>()
                    .GetValue("RateLimiting:PublicBookingsPerHour", DefaultBookingsPerHour);
                return RateLimitPartition.GetFixedWindowLimiter(ClientKey(httpContext), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limit,
                    Window = TimeSpan.FromHours(1),
                    QueueLimit = 0,
                });
            });
        });
        return services;
    }

    private static string ClientKey(HttpContext httpContext) =>
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
