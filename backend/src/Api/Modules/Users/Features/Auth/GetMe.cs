using Api.Modules.Users.Domain;
using Api.Shared.Errors;
using Api.Shared.Persistence;
using Api.Shared.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Users.Features.Auth;

/// <summary>GET /api/me: current user and their role in the current business.</summary>
internal static class GetMe
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/me", HandleAsync)
            .WithName("GetMe")
            .WithTags("Auth")
            .Produces<SessionUser>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

    private static async Task<IResult> HandleAsync(AppDbContext db, ITenantContext tenant, CancellationToken cancellationToken)
    {
        var userId = tenant.UserId ?? throw NotFound();
        var user = await db.Set<User>().AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userId && u.IsActive, cancellationToken) ?? throw NotFound();

        // Tenant filter + RLS: only the membership in the current business is visible.
        var membership = await db.Set<BusinessMembership>().AsNoTracking()
            .SingleOrDefaultAsync(m => m.UserId == userId, cancellationToken) ?? throw NotFound();

        return TypedResults.Ok(new SessionUser(user.Id, user.Email, user.FullName, membership.BusinessId, membership.Role));
    }

    private static AppException NotFound() =>
        AppException.Unauthorized("session_invalid", "The session is no longer valid.");
}
