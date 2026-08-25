using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceQR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GeofenceMoveIsRecorded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "GeofenceMovedAtUtc",
                table: "Locations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GeofenceMovedByEmployeeId",
                table: "Locations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GeofenceMovedMeters",
                table: "Locations",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GeofenceMovedAtUtc",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "GeofenceMovedByEmployeeId",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "GeofenceMovedMeters",
                table: "Locations");
        }
    }
}
