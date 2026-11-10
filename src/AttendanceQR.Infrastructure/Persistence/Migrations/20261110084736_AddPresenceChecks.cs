using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceQR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPresenceChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PresenceCheckSetAtUtc",
                table: "Employees",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PresenceCheckSetByEmployeeId",
                table: "Employees",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PresenceCheckUntilUtc",
                table: "Employees",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PresenceChecks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttendanceRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Slot = table.Column<int>(type: "integer", nullable: false),
                    DueAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Reached = table.Column<int>(type: "integer", nullable: true),
                    Result = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    RespondedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Late = table.Column<bool>(type: "boolean", nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true),
                    AccuracyMeters = table.Column<double>(type: "double precision", nullable: true),
                    DistanceMeters = table.Column<double>(type: "double precision", nullable: true),
                    RadiusMeters = table.Column<int>(type: "integer", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PresenceChecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PresenceChecks_AttendanceRecords_AttendanceRecordId",
                        column: x => x.AttendanceRecordId,
                        principalTable: "AttendanceRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PresenceChecks_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PresenceChecks_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PresenceChecks_AttendanceRecordId_Slot",
                table: "PresenceChecks",
                columns: new[] { "AttendanceRecordId", "Slot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PresenceChecks_EmployeeId",
                table: "PresenceChecks",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PresenceChecks_TenantId",
                table: "PresenceChecks",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PresenceChecks_TenantId_EmployeeId_CheckDate",
                table: "PresenceChecks",
                columns: new[] { "TenantId", "EmployeeId", "CheckDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PresenceChecks_TenantId_Result_DueAtUtc",
                table: "PresenceChecks",
                columns: new[] { "TenantId", "Result", "DueAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PresenceChecks");

            migrationBuilder.DropColumn(
                name: "PresenceCheckSetAtUtc",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "PresenceCheckSetByEmployeeId",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "PresenceCheckUntilUtc",
                table: "Employees");
        }
    }
}
