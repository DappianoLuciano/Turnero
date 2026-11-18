using Api.Shared.Persistence;
using Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Tenancy;

/// <summary>
/// Guard rails for the rule "every business table has business_id + RLS policy + isolation test".
/// Adding a table with business_id makes these tests fail until it has RLS and is listed in
/// <see cref="CoveredTenantTables"/> (which requires adding it to the isolation tests).
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class RlsCoverageTests(PostgresFixture db)
{
    /// <summary>Tables with business_id covered by EfTenantIsolationTests and SqlTenantIsolationTests.</summary>
    private static readonly string[] CoveredTenantTables = ["business_memberships", "services"];

    private static readonly string[] GlobalTables = ["users", "refresh_tokens", "__ef_migrations_history"];

    [Fact]
    public async Task Every_table_with_business_id_has_forced_rls_and_a_policy()
    {
        var unprotected = await DbTestHelpers.ColumnAsync(db.SuperuserConnectionString, """
            SELECT c.relname
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace AND n.nspname = 'public'
            WHERE c.relkind = 'r'
              AND (EXISTS (SELECT 1 FROM information_schema.columns col
                           WHERE col.table_schema = 'public' AND col.table_name = c.relname
                             AND col.column_name = 'business_id')
                   OR c.relname = 'businesses')
              AND (NOT c.relrowsecurity OR NOT c.relforcerowsecurity
                   OR NOT EXISTS (SELECT 1 FROM pg_policy p WHERE p.polrelid = c.oid))
            """);
        unprotected.ShouldBeEmpty("Tables missing RLS (ENABLE + FORCE + policy)");
    }

    [Fact]
    public async Task Every_tenant_table_is_covered_by_isolation_tests()
    {
        var tenantTables = await DbTestHelpers.ColumnAsync(db.SuperuserConnectionString, """
            SELECT table_name FROM information_schema.columns
            WHERE table_schema = 'public' AND column_name = 'business_id'
            ORDER BY table_name
            """);
        tenantTables.ShouldBe(CoveredTenantTables.Order().ToList(),
            "Add the new table to the isolation tests and to CoveredTenantTables.");
    }

    [Fact]
    public async Task Every_table_is_either_tenant_or_explicitly_global()
    {
        var all = await DbTestHelpers.ColumnAsync(db.SuperuserConnectionString,
            "SELECT tablename FROM pg_tables WHERE schemaname = 'public' ORDER BY tablename");
        var classified = CoveredTenantTables.Concat(GlobalTables).Append("businesses").ToHashSet();
        all.Where(t => !classified.Contains(t)).ShouldBeEmpty("Unclassified tables (tenant or global?)");
    }

    [Fact]
    public void Every_ef_entity_declares_its_tenancy()
    {
        using var ctx = TenantDb.Create(db, null);
        var undeclared = ctx.Model.GetEntityTypes()
            .Where(e => !e.IsOwned())
            .Select(e => e.ClrType)
            .Where(t => new[] { typeof(ITenantEntity), typeof(ITenantRoot), typeof(IGlobalEntity) }
                .Count(i => i.IsAssignableFrom(t)) != 1)
            .Select(t => t.Name)
            .ToList();
        undeclared.ShouldBeEmpty("Each entity must implement exactly one of ITenantEntity, ITenantRoot, IGlobalEntity");
    }

    [Fact]
    public void Every_tenant_entity_maps_business_id_column()
    {
        using var ctx = TenantDb.Create(db, null);
        var wrong = ctx.Model.GetEntityTypes()
            .Where(e => typeof(ITenantEntity).IsAssignableFrom(e.ClrType))
            .Where(e => e.FindProperty(nameof(ITenantEntity.BusinessId))?.GetColumnName() != "business_id")
            .Select(e => e.ClrType.Name)
            .ToList();
        wrong.ShouldBeEmpty();
    }
}
