using Api.Shared.Persistence;

namespace Api.Tests.Infrastructure;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class DatabaseBaselineTests(PostgresFixture db)
{
    [Fact]
    public async Task Btree_gist_extension_is_installed()
    {
        var installed = await DbTestHelpers.ScalarAsync<long>(db.SuperuserConnectionString,
            "SELECT count(*) FROM pg_extension WHERE extname = 'btree_gist'");
        installed.ShouldBe(1);
    }

    [Fact]
    public async Task Runtime_role_cannot_bypass_rls()
    {
        var bypass = await DbTestHelpers.ScalarAsync<bool>(db.SuperuserConnectionString,
            "SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = @role",
            ("role", DatabaseRoles.Runtime));
        bypass.ShouldBeFalse();
    }

    [Fact]
    public async Task Runtime_role_does_not_own_any_table()
    {
        var owned = await DbTestHelpers.ScalarAsync<long>(db.SuperuserConnectionString,
            "SELECT count(*) FROM pg_tables WHERE schemaname = 'public' AND tableowner = @role",
            ("role", DatabaseRoles.Runtime));
        owned.ShouldBe(0);
    }

    [Fact]
    public async Task Runtime_role_cannot_create_tables()
    {
        var canCreate = await DbTestHelpers.ScalarAsync<bool>(db.SuperuserConnectionString,
            "SELECT has_schema_privilege(@role, 'public', 'CREATE')",
            ("role", DatabaseRoles.Runtime));
        canCreate.ShouldBeFalse();
    }
}
