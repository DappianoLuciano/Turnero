using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Api.Shared.Errors;

/// <summary>Translates exceptions into Problem Details (RFC 9457).</summary>
internal sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails problem;
        if (exception is AppException app)
        {
            problem = new ProblemDetails
            {
                Status = app.StatusCode,
                Title = app.Title,
                Detail = app.Detail,
                Type = $"https://httpstatuses.io/{app.StatusCode}",
            };
            problem.Extensions["code"] = app.Code;
        }
        else if (exception is BadHttpRequestException bad)
        {
            problem = new ProblemDetails
            {
                Status = bad.StatusCode,
                Title = "Invalid request.",
                Type = $"https://httpstatuses.io/{bad.StatusCode}",
            };
            problem.Extensions["code"] = "invalid_request";
        }
        else
        {
            logger.LogError(exception, "Unhandled exception");
            problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Type = "https://httpstatuses.io/500",
            };
            problem.Extensions["code"] = "internal_error";
        }

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
