using System.Security.Claims;
using Api.Shared.Auth;

namespace Api.Shared.Tenancy;

/// <summary>Strategy to find the tenant for an incoming request.</summary>
public interface ITenantResolver
{
    /// <summary>Returns the business id, or null if this resolver does not apply to the request.</summary>
    ValueTask<Guid?> ResolveAsync(HttpContext httpContext);
}

/// <summary>Panel (/app, /api): tenant comes from the <c>business_id</c> claim of the access token.</summary>
public sealed class ClaimsTenantResolver : ITenantResolver
{
    public ValueTask<Guid?> ResolveAsync(HttpContext httpContext) =>
        ValueTask.FromResult(ReadGuid(httpContext.User, AppClaims.BusinessId));

    public static Guid? ReadGuid(ClaimsPrincipal user, string claim) =>
        Guid.TryParse(user.FindFirstValue(claim), out var value) ? value : null;
}

/// <summary>
/// Sets <see cref="TenantContext"/> for authenticated requests. Runs after authentication and
/// before any endpoint opens a database transaction.
/// </summary>
internal sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext httpContext, TenantContext tenant, ClaimsTenantResolver resolver)
    {
        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            var businessId = await resolver.ResolveAsync(httpContext);
            var userId = ClaimsTenantResolver.ReadGuid(httpContext.User, AppClaims.Subject);
            tenant.Set(businessId, userId);
        }

        await next(httpContext);
    }
}
