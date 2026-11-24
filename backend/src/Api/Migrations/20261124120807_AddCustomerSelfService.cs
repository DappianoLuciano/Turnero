using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerSelfService : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "customer_change_notice_hours",
                table: "businesses",
                type: "integer",
                nullable: false,
                defaultValue: 12);

            migrationBuilder.AddColumn<string>(
                name: "phone",
                table: "businesses",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reminder_sent_at",
                table: "appointments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("""
                ALTER TABLE businesses
                    ADD CONSTRAINT ck_businesses_customer_change_notice_hours
                    CHECK (customer_change_notice_hours BETWEEN 0 AND 720);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE businesses DROP CONSTRAINT ck_businesses_customer_change_notice_hours;");

            migrationBuilder.DropColumn(
                name: "customer_change_notice_hours",
                table: "businesses");

            migrationBuilder.DropColumn(
                name: "phone",
                table: "businesses");

            migrationBuilder.DropColumn(
                name: "reminder_sent_at",
                table: "appointments");
        }
    }
}
