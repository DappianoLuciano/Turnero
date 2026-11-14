using System.Diagnostics;

namespace Api.Shared.Errors;

internal static class ErrorsServiceCollectionExtensions
{
    public static IServiceCollection AddProblemDetailsErrors(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = ctx =>
            {
                ctx.ProblemDetails.Instance ??= $"{ctx.HttpContext.Request.Method} {ctx.HttpContext.Request.Path}";
                ctx.ProblemDetails.Extensions["traceId"] =
                    Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier;
            };
        });
        services.AddExceptionHandler<GlobalExceptionHandler>();
        return services;
    }
}
