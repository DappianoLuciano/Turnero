using Api.Modules.Users.Domain;
using Api.Shared.Errors;
using Api.Shared.Persistence;
using Api.Shared.Tenancy;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Users.Features.Auth;

/// <summary>POST /api/auth/login: email + password → access token (body) + refresh token (cookie).</summary>
internal static class Login
{
    public sealed record LoginRequest(string Email, string Password);

    internal sealed class Validator : AbstractValidator<LoginRequest>
    {
        public Validator()
        {
            RuleFor(r => r.Email).NotEmpty().WithErrorCode("required")
                .MaximumLength(User.EmailMaxLength).WithErrorCode("too_long")
                .EmailAddress().WithErrorCode("invalid_email");
            RuleFor(r => r.Password).NotEmpty().WithErrorCode("required")
                .MaximumLength(128).WithErrorCode("too_long");
        }
    }

    public static void Map(RouteGroupBuilder group) =>
        group.MapPost("/auth/login", HandleAsync)
            .WithName("Login")
            .WithTags("Auth")
            .Produces<SessionResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

    private static async Task<IResult> HandleAsync(
        LoginRequest request,
        AppDbContext db,
        TenantScope scope,
        IPasswordHasher<User> hasher,
        AuthSessionIssuer issuer,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var email = User.NormalizeEmail(request.Email);

        // users is a global table (no RLS): readable before the tenant is known.
        var user = await db.Set<User>().SingleOrDefaultAsync(u => u.Email == email, cancellationToken);
        if (user is null)
        {
            // Same work as a real check, so response time does not reveal whether the email exists.
            hasher.VerifyHashedPassword(DummyUser.Instance, DummyUser.Hash, request.Password);
            throw InvalidCredentials();
        }

        var verification = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed || !user.IsActive)
        {
            throw InvalidCredentials();
        }

        var (response, refresh) = await scope.RunAsync(null, user.Id, async ct =>
        {
            if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.SetPasswordHash(hasher.HashPassword(user, request.Password));
            }

            // No business yet: RLS policy member_self_read exposes only this user's memberships.
            // The EF tenant filter is bypassed on purpose; RLS remains the guard.
            var membership = await db.Set<BusinessMembership>()
                .IgnoreQueryFilters()
                .Where(m => m.UserId == user.Id)
                .OrderBy(m => m.CreatedAt)
                .FirstOrDefaultAsync(ct)
                ?? throw AppException.Forbidden("no_membership", "The user does not belong to any business.");

            var issued = issuer.Issue(user, membership);
            await db.SaveChangesAsync(ct);
            return issued;
        }, cancellationToken);

        AuthSessionIssuer.WriteRefreshCookie(http.Response, refresh);
        return TypedResults.Ok(response);
    }

    private static AppException InvalidCredentials() =>
        AppException.Unauthorized("invalid_credentials", "Invalid email or password.");

    private static class DummyUser
    {
        public static readonly User Instance = User.Create("dummy@invalid", "dummy", DateTimeOffset.UnixEpoch);
        public static readonly string Hash = new PasswordHasher<User>().HashPassword(Instance, "dummy-password");
    }
}
