using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceQR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ShiftAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "WorkCycleAnchor",
                table: "Schedules",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkCycleDays",
                table: "Schedules",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkCycleOnDays",
                table: "Schedules",
                type: "integer",
                nullable: false,
                // 1, not EF's 0: a cycle with zero working days is not a value any rotation can
                // hold. Same reasoning as the Employees column in WorkCycleRotation.
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "ScheduleId",
                table: "Employees",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WorkCycleAnchor",
                table: "Schedules");

            migrationBuilder.DropColumn(
                name: "WorkCycleDays",
                table: "Schedules");

            migrationBuilder.DropColumn(
                name: "WorkCycleOnDays",
                table: "Schedules");

            migrationBuilder.DropColumn(
                name: "ScheduleId",
                table: "Employees");
        }
    }
}
