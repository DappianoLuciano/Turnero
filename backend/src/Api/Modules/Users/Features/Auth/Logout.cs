using Api.Modules.Users.Domain;
using Api.Shared.Auth;
using Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Users.Features.Auth;

/// <summary>POST /api/auth/logout: revokes the current refresh token and clears the cookie. Idempotent.</summary>
internal static class Logout
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapPost("/auth/logout", HandleAsync)
            .WithName("Logout")
            .WithTags("Auth")
            .Produces(StatusCodes.Status204NoContent);

    private static async Task<IResult> HandleAsync(
        AppDbContext db, TimeProvider clock, HttpContext http, CancellationToken cancellationToken)
    {
        var raw = AuthSessionIssuer.ReadRefreshCookie(http.Request);
        if (raw is not null)
        {
            var hash = TokenService.HashRefreshToken(raw);
            var now = clock.GetUtcNow();
            await db.Set<RefreshToken>()
                .Where(t => t.TokenHash == hash && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), cancellationToken);
        }

        AuthSessionIssuer.DeleteRefreshCookie(http.Response);
        return TypedResults.NoContent();
    }
}
