using System;
using Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStaff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "professional_services",
                columns: table => new
                {
                    professional_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_professional_services", x => new { x.professional_id, x.service_id });
                });

            migrationBuilder.CreateTable(
                name: "professionals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_professionals", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_professional_services_service_id",
                table: "professional_services",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_professionals_business_id_name",
                table: "professionals",
                columns: new[] { "business_id", "name" });

            // Cross-module integrity without mapping other modules' entities in EF.
            // Deleting a professional or a service removes the link between them.
            migrationBuilder.Sql("""
                ALTER TABLE professionals
                    ADD CONSTRAINT fk_professionals_businesses_business_id
                    FOREIGN KEY (business_id) REFERENCES businesses (id) ON DELETE CASCADE;
                ALTER TABLE professional_services
                    ADD CONSTRAINT fk_professional_services_businesses_business_id
                    FOREIGN KEY (business_id) REFERENCES businesses (id) ON DELETE CASCADE,
                    ADD CONSTRAINT fk_professional_services_professionals_professional_id
                    FOREIGN KEY (professional_id) REFERENCES professionals (id) ON DELETE CASCADE,
                    ADD CONSTRAINT fk_professional_services_services_service_id
                    FOREIGN KEY (service_id) REFERENCES services (id) ON DELETE CASCADE;
                """);

            migrationBuilder.EnableTenantRls("professionals");
            migrationBuilder.EnableTenantRls("professional_services");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DisableTenantRls("professional_services");
            migrationBuilder.DisableTenantRls("professionals");
            migrationBuilder.DropTable(
                name: "professional_services");

            migrationBuilder.DropTable(
                name: "professionals");
        }
    }
}
