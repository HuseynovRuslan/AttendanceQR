using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceQR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShiftOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShiftOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    ScheduleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    CreatedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftOverrides", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShiftOverrides_TenantId_Date",
                table: "ShiftOverrides",
                columns: new[] { "TenantId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_ShiftOverrides_TenantId_EmployeeId_Date",
                table: "ShiftOverrides",
                columns: new[] { "TenantId", "EmployeeId", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShiftOverrides");
        }
    }
}
