using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceQR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScheduleBelongsToBranch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                table: "Schedules",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Schedules_LocationId",
                table: "Schedules",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Schedules_TenantId_LocationId",
                table: "Schedules",
                columns: new[] { "TenantId", "LocationId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Schedules_Locations_LocationId",
                table: "Schedules",
                column: "LocationId",
                principalTable: "Locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Schedules_Locations_LocationId",
                table: "Schedules");

            migrationBuilder.DropIndex(
                name: "IX_Schedules_LocationId",
                table: "Schedules");

            migrationBuilder.DropIndex(
                name: "IX_Schedules_TenantId_LocationId",
                table: "Schedules");

            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "Schedules");
        }
    }
}
