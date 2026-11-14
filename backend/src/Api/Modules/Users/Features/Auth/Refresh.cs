using Api.Modules.Users.Domain;
using Api.Shared.Auth;
using Api.Shared.Persistence;
using Api.Shared.Tenancy;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Users.Features.Auth;

/// <summary>
/// POST /api/auth/refresh: rotates the refresh token (cookie) and returns a new access token.
/// Reusing an already-rotated token revokes its whole family (stolen-token detection).
/// </summary>
internal static class Refresh
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapPost("/auth/refresh", HandleAsync)
            .WithName("RefreshSession")
            .WithTags("Auth")
            .Produces<SessionResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

    private static async Task<IResult> HandleAsync(
        AppDbContext db,
        TenantScope scope,
        AuthSessionIssuer issuer,
        TimeProvider clock,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var raw = AuthSessionIssuer.ReadRefreshCookie(http.Request);
        if (raw is null)
        {
            return Invalid(http);
        }

        var hash = TokenService.HashRefreshToken(raw);
        var now = clock.GetUtcNow();

        // refresh_tokens is global: looked up before the tenant is known.
        var stored = await db.Set<RefreshToken>().SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (stored is null)
        {
            return Invalid(http);
        }

        if (stored.RevokedAt is not null)
        {
            await db.Set<RefreshToken>()
                .Where(t => t.FamilyId == stored.FamilyId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), cancellationToken);
            return Invalid(http);
        }

        if (stored.ExpiresAt <= now)
        {
            return Invalid(http);
        }

        var user = await db.Set<User>().SingleOrDefaultAsync(u => u.Id == stored.UserId && u.IsActive, cancellationToken);
        if (user is null)
        {
            return Invalid(http);
        }

        var issued = await scope.RunAsync(stored.IssuedForBusinessId, user.Id, async ct =>
        {
            // Atomic claim: protects against two concurrent refreshes with the same token.
            var claimed = await db.Set<RefreshToken>()
                .Where(t => t.Id == stored.Id && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
            if (claimed == 0)
            {
                return ((SessionResponse, IssuedRefreshToken)?)null;
            }

            // Membership may have been removed since login: tenant filter + RLS apply here.
            var membership = await db.Set<BusinessMembership>().SingleOrDefaultAsync(m => m.UserId == user.Id, ct);
            if (membership is null)
            {
                return null;
            }

            var result = issuer.Issue(user, membership, stored.FamilyId, stored);
            await db.SaveChangesAsync(ct);
            return result;
        }, cancellationToken);

        if (issued is not { } session)
        {
            return Invalid(http);
        }

        AuthSessionIssuer.WriteRefreshCookie(http.Response, session.Item2);
        return TypedResults.Ok(session.Item1);
    }

    private static ProblemHttpResult Invalid(HttpContext http)
    {
        AuthSessionIssuer.DeleteRefreshCookie(http.Response);
        return TypedResults.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "The session has expired. Please log in again.",
            extensions: new Dictionary<string, object?> { ["code"] = "invalid_refresh_token" });
    }
}
