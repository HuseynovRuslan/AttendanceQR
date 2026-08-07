using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceQR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFieldVisits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FieldVisits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    VisitDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TargetLabel = table.Column<string>(type: "text", nullable: true),
                    TargetLatitude = table.Column<double>(type: "double precision", nullable: true),
                    TargetLongitude = table.Column<double>(type: "double precision", nullable: true),
                    TargetRadiusMeters = table.Column<int>(type: "integer", nullable: true),
                    AssignedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CheckInAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CheckInLatitude = table.Column<double>(type: "double precision", nullable: true),
                    CheckInLongitude = table.Column<double>(type: "double precision", nullable: true),
                    CheckInPhotoKey = table.Column<string>(type: "text", nullable: true),
                    CheckInDistanceMeters = table.Column<double>(type: "double precision", nullable: true),
                    CheckOutAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CheckOutLatitude = table.Column<double>(type: "double precision", nullable: true),
                    CheckOutLongitude = table.Column<double>(type: "double precision", nullable: true),
                    CheckOutPhotoKey = table.Column<string>(type: "text", nullable: true),
                    Note = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FieldVisits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FieldVisits_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FieldVisits_TenantId",
                table: "FieldVisits",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_FieldVisits_TenantId_EmployeeId_VisitDate",
                table: "FieldVisits",
                columns: new[] { "TenantId", "EmployeeId", "VisitDate" });

            migrationBuilder.CreateIndex(
                name: "IX_FieldVisits_TenantId_VisitDate",
                table: "FieldVisits",
                columns: new[] { "TenantId", "VisitDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FieldVisits");
        }
    }
}
