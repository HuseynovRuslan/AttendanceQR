using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceQR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceSelfCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SelfCorrectionNeedsApproval",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AttendanceSelfCorrections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorEmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    AttendanceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CheckInBeforeUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CheckOutBeforeUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CheckInAfterUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CheckOutAfterUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AwaitsApproval = table.Column<bool>(type: "boolean", nullable: false),
                    DecisionForId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceSelfCorrections", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceSelfCorrections_DecisionForId",
                table: "AttendanceSelfCorrections",
                column: "DecisionForId",
                unique: true,
                filter: "\"DecisionForId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceSelfCorrections_TenantId_AtUtc",
                table: "AttendanceSelfCorrections",
                columns: new[] { "TenantId", "AtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceSelfCorrections_TenantId_EmployeeId_AttendanceDate",
                table: "AttendanceSelfCorrections",
                columns: new[] { "TenantId", "EmployeeId", "AttendanceDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceSelfCorrections");

            migrationBuilder.DropColumn(
                name: "SelfCorrectionNeedsApproval",
                table: "Tenants");
        }
    }
}
