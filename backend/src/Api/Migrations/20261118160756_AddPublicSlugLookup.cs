using Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicSlugLookup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The public booking site (ADR 0012) has no tenant yet when it must find the business of a slug. This
            // second, PERMISSIVE policy (OR-ed with tenant_isolation) allows SELECT of exactly one row: the one whose
            // slug equals app.public_slug, a setting only the public endpoint filter sets, per request, in a short
            // transaction. It never allows writes and never exposes other businesses.
            migrationBuilder.Sql($"""
                CREATE POLICY public_slug_lookup ON businesses
                    FOR SELECT
                    USING (slug = NULLIF(current_setting('{TenantSettings.PublicSlug}', true), ''));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS public_slug_lookup ON businesses;");
        }
    }
}
