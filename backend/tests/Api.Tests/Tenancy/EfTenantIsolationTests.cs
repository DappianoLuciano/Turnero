using Api.Modules.Businesses.Domain;
using Api.Modules.Users.Domain;
using Api.Shared.Auth;
using Api.Shared.Tenancy;
using Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Api.Tests.Tenancy;

/// <summary>
/// With the context of business A, nothing of business B can be read or modified through EF —
/// not even when the EF query filter or the EF write guard are bypassed (RLS is the second layer).
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class EfTenantIsolationTests(PostgresFixture db) : IAsyncLifetime
{
    private TwoTenants _t = null!;

    public async ValueTask InitializeAsync() => _t = await TwoTenants.CreateAsync(db);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Reads_only_rows_of_the_current_tenant()
    {
        await using var ctx = TenantDb.Create(db, _t.A.BusinessId);
        await using var tx = await ctx.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);

        var memberships = await ctx.Set<BusinessMembership>().ToListAsync(TestContext.Current.CancellationToken);
        memberships.Count.ShouldBe(2);
        memberships.ShouldAllBe(m => m.BusinessId == _t.A.BusinessId);

        var businesses = await ctx.Set<Business>().ToListAsync(TestContext.Current.CancellationToken);
        businesses.Select(b => b.Id).ShouldBe([_t.A.BusinessId]);
    }

    [Fact]
    public async Task Rls_hides_other_tenant_even_when_ef_filter_is_ignored()
    {
        await using var ctx = TenantDb.Create(db, _t.A.BusinessId);
        await using var tx = await ctx.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);

        var memberships = await ctx.Set<BusinessMembership>().IgnoreQueryFilters()
            .Where(m => m.BusinessId == _t.B.BusinessId)
            .ToListAsync(TestContext.Current.CancellationToken);
        memberships.ShouldBeEmpty();

        var allVisibleBusinesses = await ctx.Set<Business>().IgnoreQueryFilters()
            .Select(b => b.Id).ToListAsync(TestContext.Current.CancellationToken);
        allVisibleBusinesses.ShouldBe([_t.A.BusinessId]);
    }

    [Fact]
    public async Task Bulk_update_and_delete_of_other_tenant_affect_zero_rows()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var ctx = TenantDb.Create(db, _t.A.BusinessId))
        {
            await using var tx = await ctx.Database.BeginTransactionAsync(ct);

            var updated = await ctx.Set<BusinessMembership>().IgnoreQueryFilters()
                .Where(m => m.BusinessId == _t.B.BusinessId)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Role, BusinessRole.Reception), ct);
            var deleted = await ctx.Set<BusinessMembership>().IgnoreQueryFilters()
                .Where(m => m.BusinessId == _t.B.BusinessId)
                .ExecuteDeleteAsync(ct);
            var businessRenamed = await ctx.Set<Business>().IgnoreQueryFilters()
                .Where(b => b.Id == _t.B.BusinessId)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.Name, "hacked"), ct);

            updated.ShouldBe(0);
            deleted.ShouldBe(0);
            businessRenamed.ShouldBe(0);
            await tx.CommitAsync(ct);
        }

        await AssertTenantBUntouchedAsync();
    }

    [Fact]
    public async Task Ef_guard_rejects_modifying_an_entity_of_other_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        Guid victimId;
        await using (var ctxB = TenantDb.Create(db, _t.B.BusinessId))
        {
            await using var txB = await ctxB.Database.BeginTransactionAsync(ct);
            victimId = (await ctxB.Set<BusinessMembership>().FirstAsync(ct)).Id;
        }

        await using var ctx = TenantDb.Create(db, _t.A.BusinessId);
        await using var tx = await ctx.Database.BeginTransactionAsync(ct);
        var forged = BusinessMembership.Create(_t.B.BusinessId, _t.B.StaffId, BusinessRole.Owner, DateTimeOffset.UtcNow);
        ctx.Entry(forged).Property(m => m.Id).CurrentValue = victimId;
        ctx.Attach(forged).State = EntityState.Modified;

        await Should.ThrowAsync<CrossTenantAccessException>(() => ctx.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task Ef_guard_rejects_inserting_rows_for_other_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = TenantDb.Create(db, _t.A.BusinessId);
        await using var tx = await ctx.Database.BeginTransactionAsync(ct);
        ctx.Add(BusinessMembership.Create(_t.B.BusinessId, _t.A.OwnerId, BusinessRole.Owner, DateTimeOffset.UtcNow));

        await Should.ThrowAsync<CrossTenantAccessException>(() => ctx.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task Rls_rejects_insert_for_other_tenant_when_ef_guard_is_absent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = TenantDb.Create(db, _t.A.BusinessId, withGuard: false);
        await using var tx = await ctx.Database.BeginTransactionAsync(ct);
        ctx.Add(BusinessMembership.Create(_t.B.BusinessId, _t.A.OwnerId, BusinessRole.Owner, DateTimeOffset.UtcNow));

        var ex = await Should.ThrowAsync<DbUpdateException>(() => ctx.SaveChangesAsync(ct));
        ex.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task Rls_rejects_update_of_other_tenant_when_ef_guard_is_absent()
    {
        var ct = TestContext.Current.CancellationToken;
        Guid victimId;
        await using (var ctxB = TenantDb.Create(db, _t.B.BusinessId))
        {
            await using var txB = await ctxB.Database.BeginTransactionAsync(ct);
            victimId = (await ctxB.Set<BusinessMembership>().FirstAsync(ct)).Id;
        }

        await using var ctx = TenantDb.Create(db, _t.A.BusinessId, withGuard: false);
        await using var tx = await ctx.Database.BeginTransactionAsync(ct);
        var forged = BusinessMembership.Create(_t.B.BusinessId, _t.B.StaffId, BusinessRole.Owner, DateTimeOffset.UtcNow);
        ctx.Entry(forged).Property(m => m.Id).CurrentValue = victimId;
        ctx.Attach(forged).State = EntityState.Modified;

        // RLS hides the row: the UPDATE matches 0 rows and EF reports a concurrency failure.
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => ctx.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task Without_tenant_nothing_is_visible()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = TenantDb.Create(db, businessId: null);
        await using var tx = await ctx.Database.BeginTransactionAsync(ct);

        (await ctx.Set<BusinessMembership>().IgnoreQueryFilters().CountAsync(ct)).ShouldBe(0);
        (await ctx.Set<Business>().IgnoreQueryFilters().CountAsync(ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Outside_a_transaction_rls_returns_nothing()
    {
        // Settings are transaction-local: a query without a transaction has no tenant (fail closed).
        await using var ctx = TenantDb.Create(db, _t.A.BusinessId);
        (await ctx.Set<BusinessMembership>().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    private async Task AssertTenantBUntouchedAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctxB = TenantDb.Create(db, _t.B.BusinessId);
        await using var txB = await ctxB.Database.BeginTransactionAsync(ct);
        var memberships = await ctxB.Set<BusinessMembership>().ToListAsync(ct);
        memberships.Count.ShouldBe(2);
        memberships.ShouldNotContain(m => m.Role == BusinessRole.Reception);
        (await ctxB.Set<Business>().SingleAsync(ct)).Name.ShouldBe("Business B");
    }
}
