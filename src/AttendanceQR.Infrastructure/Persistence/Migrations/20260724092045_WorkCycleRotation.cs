using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceQR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkCycleRotation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "WorkCycleAnchor",
                table: "Employees",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkCycleDays",
                table: "Employees",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkCycleOnDays",
                table: "Employees",
                type: "integer",
                nullable: false,
                // 1, not EF's 0 default: 0 working days is not a thing any cycle can mean, and every
                // existing row gets this value. Harmless either way (WorkCycleDays is null, so no
                // rotation is in play) but a column should never hold a value it cannot legally have.
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WorkCycleAnchor",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "WorkCycleDays",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "WorkCycleOnDays",
                table: "Employees");
        }
    }
}
