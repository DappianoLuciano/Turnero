using System;
using Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "appointments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    professional_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_appointments", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_appointments_business_id_starts_at",
                table: "appointments",
                columns: new[] { "business_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_appointments_customer_id",
                table: "appointments",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_professional_id_starts_at",
                table: "appointments",
                columns: new[] { "professional_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_appointments_service_id",
                table: "appointments",
                column: "service_id");

            // Cross-module integrity without mapping other modules' entities in EF. Customers, professionals and
            // services are NO ACTION (not cascade): with history they cannot be deleted, only archived/deactivated.
            migrationBuilder.Sql("""
                ALTER TABLE appointments
                    ADD CONSTRAINT fk_appointments_businesses_business_id
                    FOREIGN KEY (business_id) REFERENCES businesses (id) ON DELETE CASCADE,
                    ADD CONSTRAINT fk_appointments_customers_customer_id
                    FOREIGN KEY (customer_id) REFERENCES customers (id),
                    ADD CONSTRAINT fk_appointments_professionals_professional_id
                    FOREIGN KEY (professional_id) REFERENCES professionals (id),
                    ADD CONSTRAINT fk_appointments_services_service_id
                    FOREIGN KEY (service_id) REFERENCES services (id),
                    ADD CONSTRAINT ck_appointments_period CHECK (ends_at > starts_at),
                    ADD CONSTRAINT ck_appointments_price CHECK (price >= 0),
                    ADD CONSTRAINT ck_appointments_status CHECK (status IN ('Scheduled', 'Completed', 'Cancelled', 'NoShow'));
                """);

            // A professional cannot have two appointments that overlap in time while they still occupy their slot.
            // Ranges are half-open: an appointment may start exactly when the previous one ends.
            migrationBuilder.Sql("""
                ALTER TABLE appointments
                    ADD CONSTRAINT ex_appointments_professional_no_overlap
                    EXCLUDE USING gist (professional_id WITH =, tstzrange(starts_at, ends_at, '[)') WITH &&)
                    WHERE (status IN ('Scheduled', 'Completed'));
                """);

            migrationBuilder.EnableTenantRls("appointments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DisableTenantRls("appointments");
            migrationBuilder.DropTable(
                name: "appointments");
        }
    }
}
