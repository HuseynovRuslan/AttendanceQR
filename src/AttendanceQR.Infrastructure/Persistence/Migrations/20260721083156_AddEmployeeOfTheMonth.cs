using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceQR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeOfTheMonth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MonthlyVoteBallots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Period = table.Column<DateOnly>(type: "date", nullable: false),
                    VoterEmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonthlyVoteBallots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MonthlyVoteBallots_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MonthlyVoteTallies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Period = table.Column<DateOnly>(type: "date", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateEmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Votes = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonthlyVoteTallies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MonthlyVoteTallies_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MonthlyWinners",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Period = table.Column<DateOnly>(type: "date", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Votes = table.Column<int>(type: "integer", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonthlyWinners", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MonthlyWinners_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyVoteBallots_TenantId",
                table: "MonthlyVoteBallots",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyVoteBallots_TenantId_Period_VoterEmployeeId",
                table: "MonthlyVoteBallots",
                columns: new[] { "TenantId", "Period", "VoterEmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyVoteTallies_TenantId",
                table: "MonthlyVoteTallies",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyVoteTallies_TenantId_Period_CandidateEmployeeId",
                table: "MonthlyVoteTallies",
                columns: new[] { "TenantId", "Period", "CandidateEmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyWinners_TenantId",
                table: "MonthlyWinners",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyWinners_TenantId_Period_LocationId",
                table: "MonthlyWinners",
                columns: new[] { "TenantId", "Period", "LocationId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MonthlyVoteBallots");

            migrationBuilder.DropTable(
                name: "MonthlyVoteTallies");

            migrationBuilder.DropTable(
                name: "MonthlyWinners");
        }
    }
}
