using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceQR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ShiftPlanRestDayAndRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsRestDay",
                table: "Schedules",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "RevisionOfId",
                table: "Schedules",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ValidUntil",
                table: "Schedules",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Schedules_RevisionOfId",
                table: "Schedules",
                column: "RevisionOfId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Schedules_RevisionOfId",
                table: "Schedules");

            migrationBuilder.DropColumn(
                name: "IsRestDay",
                table: "Schedules");

            migrationBuilder.DropColumn(
                name: "RevisionOfId",
                table: "Schedules");

            migrationBuilder.DropColumn(
                name: "ValidUntil",
                table: "Schedules");
        }
    }
}
