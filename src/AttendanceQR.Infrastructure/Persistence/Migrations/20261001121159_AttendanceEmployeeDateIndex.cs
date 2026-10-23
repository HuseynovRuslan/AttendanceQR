using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceQR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AttendanceEmployeeDateIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_TenantId_EmployeeId_AttendanceDate",
                table: "AttendanceRecords",
                columns: new[] { "TenantId", "EmployeeId", "AttendanceDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AttendanceRecords_TenantId_EmployeeId_AttendanceDate",
                table: "AttendanceRecords");
        }
    }
}
