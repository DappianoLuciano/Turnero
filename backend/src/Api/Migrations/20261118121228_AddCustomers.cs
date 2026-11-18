using System;
using Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customers", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_customers_business_id_name",
                table: "customers",
                columns: new[] { "business_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_customers_business_id_phone",
                table: "customers",
                columns: new[] { "business_id", "phone" },
                unique: true,
                filter: "phone IS NOT NULL AND NOT is_archived");

            // Cross-module integrity without mapping the other module's entity in EF.
            migrationBuilder.Sql("""
                ALTER TABLE customers
                    ADD CONSTRAINT fk_customers_businesses_business_id
                    FOREIGN KEY (business_id) REFERENCES businesses (id) ON DELETE CASCADE;
                """);

            migrationBuilder.EnableTenantRls("customers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DisableTenantRls("customers");
            migrationBuilder.DropTable(
                name: "customers");
        }
    }
}
