using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceQR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceBindingOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 0 = Activation. True for every existing row except the handful an admin approved as a
            // device change; there is nothing on the row to tell them apart after the fact, and the
            // approvals are in the audit log, so the approximation stands rather than guessing.
            migrationBuilder.AddColumn<int>(
                name: "BoundVia",
                table: "DeviceBindings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "RevokedAtUtc",
                table: "DeviceBindings",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BoundVia",
                table: "DeviceBindings");

            migrationBuilder.DropColumn(
                name: "RevokedAtUtc",
                table: "DeviceBindings");
        }
    }
}
