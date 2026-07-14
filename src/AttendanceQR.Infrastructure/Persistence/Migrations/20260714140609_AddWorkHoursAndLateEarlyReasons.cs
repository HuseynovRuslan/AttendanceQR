using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceQR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkHoursAndLateEarlyReasons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeOnly>(
                name: "WorkEnd",
                table: "Employees",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "WorkStart",
                table: "Employees",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EarlyDepartureReason",
                table: "AttendanceRecords",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LateArrivalReason",
                table: "AttendanceRecords",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WorkEnd",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "WorkStart",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "EarlyDepartureReason",
                table: "AttendanceRecords");

            migrationBuilder.DropColumn(
                name: "LateArrivalReason",
                table: "AttendanceRecords");
        }
    }
}
