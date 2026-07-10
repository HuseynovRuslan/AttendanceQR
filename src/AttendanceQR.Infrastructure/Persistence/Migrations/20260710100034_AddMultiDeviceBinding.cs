using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceQR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiDeviceBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DeviceBindings_EmployeeId",
                table: "DeviceBindings");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSeenAtUtc",
                table: "DeviceBindings",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Existing rows would otherwise carry year 0001 and be first in line for least-recently-used
            // eviction — i.e. every employee's only working device would be dropped by their next scan
            // from a second context. Seed the column from when the binding was made.
            migrationBuilder.Sql(@"UPDATE ""DeviceBindings"" SET ""LastSeenAtUtc"" = ""BoundAtUtc"";");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceBindings_EmployeeId_DeviceFingerprint",
                table: "DeviceBindings",
                columns: new[] { "EmployeeId", "DeviceFingerprint" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DeviceBindings_EmployeeId_DeviceFingerprint",
                table: "DeviceBindings");

            migrationBuilder.DropColumn(
                name: "LastSeenAtUtc",
                table: "DeviceBindings");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceBindings_EmployeeId",
                table: "DeviceBindings",
                column: "EmployeeId",
                unique: true);
        }
    }
}
