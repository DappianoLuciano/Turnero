using Api.Modules.Users.Contracts;
using Api.Modules.Users.Domain;
using Api.Shared.Auth;
using Api.Shared.Errors;
using Api.Shared.Persistence;
using Api.Shared.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Users.Features;

internal sealed class UserProvisioning(
    AppDbContext db, TenantScope scope, IPasswordHasher<User> hasher, TimeProvider clock) : IUserProvisioning
{
    public const int PasswordMinLength = 8;

    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalized = User.NormalizeEmail(email);
        // users is a global table (no RLS).
        return db.Set<User>().AnyAsync(u => u.Email == normalized, cancellationToken);
    }

    public async Task<Guid> RegisterOwnerAsync(Guid businessId, string email, string fullName, string password,
        CancellationToken cancellationToken = default)
    {
        if (password.Length < PasswordMinLength)
        {
            throw new ArgumentException($"Password must have at least {PasswordMinLength} characters.", nameof(password));
        }

        return await scope.RunAsync(businessId, null, async ct =>
        {
            var normalized = User.NormalizeEmail(email);
            if (await db.Set<User>().AnyAsync(u => u.Email == normalized, ct))
            {
                throw AppException.Conflict("email_taken", "The email is already registered.");
            }

            var now = clock.GetUtcNow();
            var user = User.Create(normalized, fullName, now);
            user.SetPasswordHash(hasher.HashPassword(user, password));
            db.Set<User>().Add(user);
            db.Set<BusinessMembership>().Add(BusinessMembership.Create(businessId, user.Id, BusinessRole.Owner, now));
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
            {
                // Two signups with the same email at once: the unique index decides.
                throw AppException.Conflict("email_taken", "The email is already registered.");
            }

            return user.Id;
        }, cancellationToken);
    }

    public async Task<Guid> AddMemberAsync(Guid businessId, string email, string fullName, string password,
        BusinessRole role, CancellationToken cancellationToken = default)
    {
        if (password.Length < PasswordMinLength)
        {
            throw new ArgumentException($"Password must have at least {PasswordMinLength} characters.", nameof(password));
        }

        return await scope.RunAsync(businessId, null, async ct =>
        {
            var normalized = User.NormalizeEmail(email);
            var user = await db.Set<User>().SingleOrDefaultAsync(u => u.Email == normalized, ct);
            if (user is null)
            {
                user = User.Create(normalized, fullName, clock.GetUtcNow());
                user.SetPasswordHash(hasher.HashPassword(user, password));
                db.Set<User>().Add(user);
            }

            var exists = await db.Set<BusinessMembership>().AnyAsync(m => m.UserId == user.Id, ct);
            if (!exists)
            {
                db.Set<BusinessMembership>().Add(BusinessMembership.Create(businessId, user.Id, role, clock.GetUtcNow()));
            }

            await db.SaveChangesAsync(ct);
            return user.Id;
        }, cancellationToken);
    }
}
