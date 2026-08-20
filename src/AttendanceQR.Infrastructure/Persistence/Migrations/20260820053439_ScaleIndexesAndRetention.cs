using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceQR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScaleIndexesAndRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ProcessedScans_TenantId_ProcessedAtUtc",
                table: "ProcessedScans",
                columns: new[] { "TenantId", "ProcessedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeNotifications_TenantId_CreatedAtUtc",
                table: "EmployeeNotifications",
                columns: new[] { "TenantId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DailySummaries_TenantId_SummaryDate",
                table: "DailySummaries",
                columns: new[] { "TenantId", "SummaryDate" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId_CreatedAtUtc",
                table: "AuditLogs",
                columns: new[] { "TenantId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_Open",
                table: "AttendanceRecords",
                columns: new[] { "TenantId", "AttendanceDate" },
                filter: "\"CheckInAtUtc\" IS NOT NULL AND \"CheckOutAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_TenantId_AttendanceDate",
                table: "AttendanceRecords",
                columns: new[] { "TenantId", "AttendanceDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProcessedScans_TenantId_ProcessedAtUtc",
                table: "ProcessedScans");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeNotifications_TenantId_CreatedAtUtc",
                table: "EmployeeNotifications");

            migrationBuilder.DropIndex(
                name: "IX_DailySummaries_TenantId_SummaryDate",
                table: "DailySummaries");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_TenantId_CreatedAtUtc",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceRecords_Open",
                table: "AttendanceRecords");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceRecords_TenantId_AttendanceDate",
                table: "AttendanceRecords");
        }
    }
}
