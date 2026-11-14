using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Api.Hosting;

internal static class HealthSetup
{
    public static IEndpointRouteBuilder MapApiHealth(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = WriteJsonAsync })
            .AllowAnonymous();
        return app;
    }

    private static Task WriteJsonAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(
                e => e.Key,
                e => new { status = e.Value.Status.ToString(), durationMs = e.Value.Duration.TotalMilliseconds }),
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonSerializerOptions.Web));
    }
}
