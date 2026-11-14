using Api.Modules.Users.Domain;
using Api.Shared.Auth;
using Api.Shared.Persistence;

namespace Api.Modules.Users.Features.Auth;

/// <summary>User + active business returned after login/refresh and by GET /api/me.</summary>
public sealed record SessionUser(Guid Id, string Email, string FullName, Guid BusinessId, BusinessRole Role);

/// <summary>Response of login and refresh. The refresh token travels only in an httpOnly cookie.</summary>
public sealed record SessionResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, SessionUser User);

/// <summary>Issues access + refresh tokens and manages the refresh cookie.</summary>
internal sealed class AuthSessionIssuer(AppDbContext db, TokenService tokens, TimeProvider clock)
{
    public const string RefreshCookieName = "rt";
    public const string RefreshCookiePath = "/api/auth";

    /// <summary>Creates the tokens. Must run inside a tenant scope; caller saves changes.</summary>
    public (SessionResponse Response, IssuedRefreshToken Refresh) Issue(
        User user, BusinessMembership membership, Guid? familyId = null, RefreshToken? replaces = null)
    {
        var access = tokens.CreateAccessToken(user.Id, user.Email, membership.BusinessId, membership.Role);
        var refresh = tokens.CreateRefreshToken();
        var stored = RefreshToken.Create(user.Id, membership.BusinessId, refresh.Hash,
            familyId ?? Guid.CreateVersion7(), clock.GetUtcNow(), refresh.ExpiresAt);
        db.Set<RefreshToken>().Add(stored);

        if (replaces is not null)
        {
            db.Entry(replaces).Property(nameof(RefreshToken.ReplacedByTokenId)).CurrentValue = stored.Id;
        }

        var response = new SessionResponse(access.Token, access.ExpiresAt,
            new SessionUser(user.Id, user.Email, user.FullName, membership.BusinessId, membership.Role));
        return (response, refresh);
    }

    public static void WriteRefreshCookie(HttpResponse response, IssuedRefreshToken refresh) =>
        response.Cookies.Append(RefreshCookieName, refresh.Token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = RefreshCookiePath,
            Expires = refresh.ExpiresAt,
            IsEssential = true,
        });

    public static void DeleteRefreshCookie(HttpResponse response) =>
        response.Cookies.Delete(RefreshCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = RefreshCookiePath,
        });

    public static string? ReadRefreshCookie(HttpRequest request) =>
        request.Cookies.TryGetValue(RefreshCookieName, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
}
