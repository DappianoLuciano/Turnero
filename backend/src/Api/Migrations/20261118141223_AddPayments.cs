using System;
using Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reverses_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payments_appointment_id_created_at",
                table: "payments",
                columns: new[] { "appointment_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_business_id_created_at",
                table: "payments",
                columns: new[] { "business_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_reverses_id",
                table: "payments",
                column: "reverses_id",
                unique: true,
                filter: "reverses_id IS NOT NULL");

            // Cross-module integrity with raw SQL (no EF navigations). The appointment is NO ACTION: an appointment
            // with payments can never be deleted. The sign of the amount tells collections from reversals.
            migrationBuilder.Sql("""
                ALTER TABLE payments
                    ADD CONSTRAINT fk_payments_businesses_business_id
                    FOREIGN KEY (business_id) REFERENCES businesses (id) ON DELETE CASCADE,
                    ADD CONSTRAINT fk_payments_appointments_appointment_id
                    FOREIGN KEY (appointment_id) REFERENCES appointments (id),
                    ADD CONSTRAINT fk_payments_payments_reverses_id
                    FOREIGN KEY (reverses_id) REFERENCES payments (id),
                    ADD CONSTRAINT ck_payments_method CHECK (method IN ('Cash', 'Card', 'Transfer', 'Other')),
                    ADD CONSTRAINT ck_payments_amount_sign CHECK (
                        (reverses_id IS NULL AND amount > 0) OR (reverses_id IS NOT NULL AND amount < 0)),
                    ADD CONSTRAINT ck_payments_reversal_reason CHECK (reverses_id IS NULL OR reason IS NOT NULL);
                """);

            migrationBuilder.EnableTenantRls("payments");

            // Append-only (ADR 0009): the application role can read and insert, never modify or delete.
            migrationBuilder.Sql($"REVOKE UPDATE, DELETE ON payments FROM {DatabaseRoles.Runtime};");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DisableTenantRls("payments");
            migrationBuilder.DropTable(
                name: "payments");
        }
    }
}
