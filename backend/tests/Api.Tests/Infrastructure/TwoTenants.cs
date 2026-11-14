using Api.Modules.Businesses.Contracts;
using Api.Modules.Users.Contracts;
using Api.Shared.Auth;
using Api.Shared.Persistence;
using Api.Shared.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Tests.Infrastructure;

public sealed record TenantData(Guid BusinessId, string Slug, Guid OwnerId, string OwnerEmail, Guid StaffId);

/// <summary>
/// Two businesses (A and B), each with an Owner and a Staff member, created through the real
/// module services. Unique slugs/emails per instance so test classes never collide.
/// </summary>
public sealed record TwoTenants(TenantData A, TenantData B)
{
    public const string Password = "Passw0rd!";

    public static async Task<TwoTenants> CreateAsync(PostgresFixture db)
    {
        var a = await CreateTenantAsync(db, "a");
        var b = await CreateTenantAsync(db, "b");
        return new TwoTenants(a, b);
    }

    private static async Task<TenantData> CreateTenantAsync(PostgresFixture db, string label)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var slug = $"{label}-{suffix}";

        BusinessSummary business;
        await using (var scope = db.Api.Services.CreateAsyncScope())
        {
            business = await scope.ServiceProvider.GetRequiredService<IBusinessProvisioning>()
                .CreateAsync($"Business {label.ToUpperInvariant()}", slug);
        }

        var ownerEmail = $"owner-{slug}@test.local";
        var ownerId = await AddMemberAsync(db, business.Id, ownerEmail, BusinessRole.Owner);
        var staffId = await AddMemberAsync(db, business.Id, $"staff-{slug}@test.local", BusinessRole.Staff);
        return new TenantData(business.Id, slug, ownerId, ownerEmail, staffId);
    }

    private static async Task<Guid> AddMemberAsync(PostgresFixture db, Guid businessId, string email, BusinessRole role)
    {
        await using var scope = db.Api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IUserProvisioning>()
            .AddMemberAsync(businessId, email, email, Password, role);
    }
}

internal static class TenantDb
{
    /// <summary>
    /// A DbContext exactly like the app's (runtime role + tenant interceptors) for a given tenant.
    /// <paramref name="withGuard"/> = false removes the EF write guard to prove RLS alone rejects writes.
    /// </summary>
    public static AppDbContext Create(PostgresFixture db, Guid? businessId, Guid? userId = null, bool withGuard = true)
    {
        var tenant = new TenantContext();
        tenant.Set(businessId, userId);
        var interceptors = withGuard
            ? new Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[]
            {
                new TenantTransactionInterceptor(tenant), new TenantGuardInterceptor(tenant),
            }
            : [new TenantTransactionInterceptor(tenant)];
        return new AppDbContext(DbContextOptionsFactory.Create(db.RuntimeConnectionString, interceptors), tenant);
    }
}
