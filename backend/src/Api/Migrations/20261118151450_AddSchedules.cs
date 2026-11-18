using System;
using Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "schedule_exceptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    professional_id = table.Column<Guid>(type: "uuid", nullable: true),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_schedule_exceptions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "working_hours",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    professional_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day_of_week = table.Column<int>(type: "integer", nullable: false),
                    start_minute = table.Column<int>(type: "integer", nullable: false),
                    end_minute = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_working_hours", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_schedule_exceptions_business_id_date",
                table: "schedule_exceptions",
                columns: new[] { "business_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_working_hours_professional_id_day_of_week",
                table: "working_hours",
                columns: new[] { "professional_id", "day_of_week" });

            migrationBuilder.Sql("""
                ALTER TABLE working_hours
                    ADD CONSTRAINT fk_working_hours_businesses_business_id
                    FOREIGN KEY (business_id) REFERENCES businesses (id) ON DELETE CASCADE,
                    ADD CONSTRAINT fk_working_hours_professionals_professional_id
                    FOREIGN KEY (professional_id) REFERENCES professionals (id) ON DELETE CASCADE,
                    ADD CONSTRAINT ck_working_hours_day CHECK (day_of_week BETWEEN 0 AND 6),
                    ADD CONSTRAINT ck_working_hours_range CHECK (
                        start_minute >= 0 AND end_minute <= 1440 AND start_minute < end_minute);

                -- A professional cannot have two overlapping blocks on the same day (half-open ranges).
                ALTER TABLE working_hours
                    ADD CONSTRAINT ex_working_hours_no_overlap
                    EXCLUDE USING gist (professional_id WITH =, day_of_week WITH =, int4range(start_minute, end_minute) WITH &&);

                ALTER TABLE schedule_exceptions
                    ADD CONSTRAINT fk_schedule_exceptions_businesses_business_id
                    FOREIGN KEY (business_id) REFERENCES businesses (id) ON DELETE CASCADE,
                    ADD CONSTRAINT fk_schedule_exceptions_professionals_professional_id
                    FOREIGN KEY (professional_id) REFERENCES professionals (id) ON DELETE CASCADE;

                -- One closure per business, day and professional (NULL professional = the whole business).
                CREATE UNIQUE INDEX ux_schedule_exceptions_scope_date ON schedule_exceptions
                    (business_id, COALESCE(professional_id, '00000000-0000-0000-0000-000000000000'::uuid), date);
                """);

            migrationBuilder.EnableTenantRls("working_hours");
            migrationBuilder.EnableTenantRls("schedule_exceptions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DisableTenantRls("schedule_exceptions");
            migrationBuilder.DisableTenantRls("working_hours");
            migrationBuilder.DropTable(
                name: "schedule_exceptions");

            migrationBuilder.DropTable(
                name: "working_hours");
        }
    }
}
