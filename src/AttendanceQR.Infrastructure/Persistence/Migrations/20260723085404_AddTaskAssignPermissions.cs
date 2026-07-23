using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceQR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskAssignPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TaskAssignPermissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignerEmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientEmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskAssignPermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskAssignPermissions_Employees_AssignerEmployeeId",
                        column: x => x.AssignerEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskAssignPermissions_Employees_RecipientEmployeeId",
                        column: x => x.RecipientEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskAssignPermissions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskAssignPermissions_AssignerEmployeeId",
                table: "TaskAssignPermissions",
                column: "AssignerEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskAssignPermissions_RecipientEmployeeId",
                table: "TaskAssignPermissions",
                column: "RecipientEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskAssignPermissions_TenantId",
                table: "TaskAssignPermissions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskAssignPermissions_TenantId_AssignerEmployeeId_Recipient~",
                table: "TaskAssignPermissions",
                columns: new[] { "TenantId", "AssignerEmployeeId", "RecipientEmployeeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaskAssignPermissions");
        }
    }
}
