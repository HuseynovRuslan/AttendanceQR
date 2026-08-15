using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceQR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropEmployeeTasks : Migration
    {
        /// <inheritdoc />
        // Drops a table that never held a row. The assigned-tasks feature shipped this morning and was
        // withdrawn the same day — the customer looked at it and decided the shared team board was the
        // one they wanted. Verified empty in production (0 rows, and no photo ever uploaded under the
        // tasks/ prefix) before this was written, which is the only reason a DropTable is safe here.
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeeTasks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmployeeTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AssignedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    DoneAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    PhotoAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PhotoKey = table.Column<string>(type: "text", nullable: true),
                    RejectionNote = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    WorkerNote = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeTasks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeTasks_TenantId_EmployeeId_Status",
                table: "EmployeeTasks",
                columns: new[] { "TenantId", "EmployeeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeTasks_TenantId_Status_DueDate",
                table: "EmployeeTasks",
                columns: new[] { "TenantId", "Status", "DueDate" });
        }
    }
}
