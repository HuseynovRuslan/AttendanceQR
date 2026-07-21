using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceQR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VoteCampaigns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VoteSettings");

            migrationBuilder.CreateTable(
                name: "VoteCampaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Period = table.Column<DateOnly>(type: "date", nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    EndsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    MinCandidates = table.Column<int>(type: "integer", nullable: false),
                    MinVotesToDecide = table.Column<int>(type: "integer", nullable: false),
                    OpenedNotifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoteCampaigns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VoteCampaigns_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VoteCampaigns_TenantId",
                table: "VoteCampaigns",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_VoteCampaigns_TenantId_Period",
                table: "VoteCampaigns",
                columns: new[] { "TenantId", "Period" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VoteCampaigns");

            migrationBuilder.CreateTable(
                name: "VoteSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    ManualFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    ManualTo = table.Column<DateOnly>(type: "date", nullable: true),
                    MinCandidates = table.Column<int>(type: "integer", nullable: false),
                    MinVotesToDecide = table.Column<int>(type: "integer", nullable: false),
                    OpenDaysBeforeEnd = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoteSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VoteSettings_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VoteSettings_TenantId",
                table: "VoteSettings",
                column: "TenantId",
                unique: true);
        }
    }
}
