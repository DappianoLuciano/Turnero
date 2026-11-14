using Microsoft.EntityFrameworkCore.Migrations;

namespace Api.Shared.Persistence;

/// <summary>Postgres role names. Created by infra/postgres/init (Docker) or the test fixture.</summary>
public static class DatabaseRoles
{
    /// <summary>Owns schema and tables; runs migrations.</summary>
    public const string Migrator = "app_migrator";

    /// <summary>Used by the API at runtime. Not owner, NOBYPASSRLS: RLS always applies.</summary>
    public const string Runtime = "app_runtime";
}

/// <summary>Postgres session settings read by the RLS policies.</summary>
public static class TenantSettings
{
    public const string BusinessId = "app.business_id";
    public const string UserId = "app.user_id";

    /// <summary>SQL expression for the current tenant. NULLIF: an unset/reset setting is '' and must match nothing.</summary>
    public const string CurrentBusinessIdSql = $"NULLIF(current_setting('{BusinessId}', true), '')::uuid";

    public const string CurrentUserIdSql = $"NULLIF(current_setting('{UserId}', true), '')::uuid";
}

/// <summary>Helpers to declare Row-Level Security inside EF migrations.</summary>
public static class RlsMigrationExtensions
{
    public const string TenantPolicyName = "tenant_isolation";

    /// <summary>
    /// Enables and FORCES RLS on <paramref name="table"/> and adds the standard tenant policy:
    /// rows are visible and writable only when <paramref name="column"/> equals <c>app.business_id</c>.
    /// Every business table must call this in the migration that creates it.
    /// </summary>
    public static void EnableTenantRls(this MigrationBuilder migrationBuilder, string table, string column = "business_id")
    {
        migrationBuilder.Sql($"""
            ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
            ALTER TABLE {table} FORCE ROW LEVEL SECURITY;
            CREATE POLICY {TenantPolicyName} ON {table}
                USING ({column} = {TenantSettings.CurrentBusinessIdSql})
                WITH CHECK ({column} = {TenantSettings.CurrentBusinessIdSql});
            """);
    }

    public static void DisableTenantRls(this MigrationBuilder migrationBuilder, string table)
    {
        migrationBuilder.Sql($"""
            DROP POLICY IF EXISTS {TenantPolicyName} ON {table};
            ALTER TABLE {table} NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;
            """);
    }
}
