using System;
using Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trial_ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    paid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscriptions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_business_id",
                table: "subscriptions",
                column: "business_id",
                unique: true);
        
            // Cross-module integrity without mapping the other module's entity in EF.
            migrationBuilder.Sql("""
                ALTER TABLE subscriptions
                    ADD CONSTRAINT fk_subscriptions_businesses_business_id
                    FOREIGN KEY (business_id) REFERENCES businesses (id) ON DELETE CASCADE;
                """);

            // Businesses that existed before subscriptions keep working: they get a long paid period.
            // businesses has FORCE RLS, so even its owner sees nothing without a tenant: lift FORCE only for this read.
            migrationBuilder.Sql("""
                ALTER TABLE businesses NO FORCE ROW LEVEL SECURITY;
                INSERT INTO subscriptions (id, business_id, trial_ends_at, paid_until, created_at, updated_at)
                SELECT gen_random_uuid(), id, now(), now() + interval '10 years', now(), now() FROM businesses;
                ALTER TABLE businesses FORCE ROW LEVEL SECURITY;
                """);

            migrationBuilder.EnableTenantRls("subscriptions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DisableTenantRls("subscriptions");
            migrationBuilder.DropTable(
                name: "subscriptions");
        }
    }
}
