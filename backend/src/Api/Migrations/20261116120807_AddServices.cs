using System;
using Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    /// <inheritdoc />
    public partial class AddServices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "services",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_services", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_services_business_id_name",
                table: "services",
                columns: new[] { "business_id", "name" });

            // Cross-module integrity without mapping the other module's entity in EF.
            migrationBuilder.Sql("""
                ALTER TABLE services
                    ADD CONSTRAINT fk_services_businesses_business_id
                    FOREIGN KEY (business_id) REFERENCES businesses (id) ON DELETE CASCADE;
                ALTER TABLE services
                    ADD CONSTRAINT ck_services_duration_minutes CHECK (duration_minutes BETWEEN 5 AND 480),
                    ADD CONSTRAINT ck_services_price CHECK (price >= 0);
                """);

            migrationBuilder.EnableTenantRls("services");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DisableTenantRls("services");
            migrationBuilder.DropTable(
                name: "services");
        }
    }
}
