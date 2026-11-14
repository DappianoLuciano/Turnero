using Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    /// <summary>
    /// Database baseline: btree_gist extension (future exclusion constraints for appointments)
    /// and privileges for the runtime role. Runs as <c>app_migrator</c>, so every table created
    /// by later migrations automatically grants DML (never DDL/ownership) to <c>app_runtime</c>.
    /// </summary>
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,");

            migrationBuilder.Sql($"""
                GRANT USAGE ON SCHEMA public TO {DatabaseRoles.Runtime};
                ALTER DEFAULT PRIVILEGES IN SCHEMA public
                    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {DatabaseRoles.Runtime};
                ALTER DEFAULT PRIVILEGES IN SCHEMA public
                    GRANT USAGE, SELECT ON SEQUENCES TO {DatabaseRoles.Runtime};
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                ALTER DEFAULT PRIVILEGES IN SCHEMA public
                    REVOKE SELECT, INSERT, UPDATE, DELETE ON TABLES FROM {DatabaseRoles.Runtime};
                ALTER DEFAULT PRIVILEGES IN SCHEMA public
                    REVOKE USAGE, SELECT ON SEQUENCES FROM {DatabaseRoles.Runtime};
                REVOKE USAGE ON SCHEMA public FROM {DatabaseRoles.Runtime};
                """);
        }
    }
}
