using System;
using Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCashRegister : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cash_closings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    expected = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    counted = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    difference = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cash_closings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cash_movements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    concept = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    reverses_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cash_movements", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cash_closings_business_id_date",
                table: "cash_closings",
                columns: new[] { "business_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cash_movements_business_id_created_at",
                table: "cash_movements",
                columns: new[] { "business_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_cash_movements_reverses_id",
                table: "cash_movements",
                column: "reverses_id",
                unique: true,
                filter: "reverses_id IS NOT NULL");

            migrationBuilder.Sql("""
                ALTER TABLE cash_movements
                    ADD CONSTRAINT fk_cash_movements_businesses_business_id
                    FOREIGN KEY (business_id) REFERENCES businesses (id) ON DELETE CASCADE,
                    ADD CONSTRAINT fk_cash_movements_cash_movements_reverses_id
                    FOREIGN KEY (reverses_id) REFERENCES cash_movements (id),
                    ADD CONSTRAINT ck_cash_movements_kind CHECK (kind IN ('Income', 'Expense')),
                    ADD CONSTRAINT ck_cash_movements_amount_sign CHECK (
                        (reverses_id IS NULL AND ((kind = 'Income' AND amount > 0) OR (kind = 'Expense' AND amount < 0)))
                        OR (reverses_id IS NOT NULL AND ((kind = 'Income' AND amount < 0) OR (kind = 'Expense' AND amount > 0)))),
                    ADD CONSTRAINT ck_cash_movements_reversal_reason CHECK (reverses_id IS NULL OR reason IS NOT NULL);

                ALTER TABLE cash_closings
                    ADD CONSTRAINT fk_cash_closings_businesses_business_id
                    FOREIGN KEY (business_id) REFERENCES businesses (id) ON DELETE CASCADE,
                    ADD CONSTRAINT ck_cash_closings_counted CHECK (counted >= 0);
                """);

            migrationBuilder.EnableTenantRls("cash_movements");
            migrationBuilder.EnableTenantRls("cash_closings");

            // Append-only (ADR 0009): the application role can read and insert, never modify or delete.
            migrationBuilder.Sql($"REVOKE UPDATE, DELETE ON cash_movements, cash_closings FROM {DatabaseRoles.Runtime};");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DisableTenantRls("cash_closings");
            migrationBuilder.DisableTenantRls("cash_movements");
            migrationBuilder.DropTable(
                name: "cash_closings");

            migrationBuilder.DropTable(
                name: "cash_movements");
        }
    }
}
