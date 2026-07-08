using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceQR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPhotoAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReferencePhotoKey",
                table: "Employees",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReferencePhotoTakenAtUtc",
                table: "Employees",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CheckInPhotoKey",
                table: "AttendanceRecords",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CheckInPhotoTakenAtUtc",
                table: "AttendanceRecords",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReferencePhotoKey",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "ReferencePhotoTakenAtUtc",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "CheckInPhotoKey",
                table: "AttendanceRecords");

            migrationBuilder.DropColumn(
                name: "CheckInPhotoTakenAtUtc",
                table: "AttendanceRecords");
        }
    }
}
