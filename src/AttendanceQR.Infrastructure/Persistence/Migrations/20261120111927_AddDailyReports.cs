using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceQR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeOnly>(
                name: "DailyReportAtLocal",
                table: "Tenants",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Headcount",
                table: "Locations",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DailyReportRecipients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    LocationIds = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    TelegramChatId = table.Column<long>(type: "bigint", nullable: true),
                    LinkedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LinkCodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LinkCodeExpiresUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyReportRecipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyReportRecipients_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DailyReportDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SnapshotJson = table.Column<string>(type: "text", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TokenExpiresUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyReportDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyReportDeliveries_DailyReportRecipients_RecipientId",
                        column: x => x.RecipientId,
                        principalTable: "DailyReportRecipients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DailyReportDeliveries_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyReportDeliveries_RecipientId",
                table: "DailyReportDeliveries",
                column: "RecipientId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyReportDeliveries_TenantId",
                table: "DailyReportDeliveries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyReportDeliveries_TenantId_RecipientId_ReportDate",
                table: "DailyReportDeliveries",
                columns: new[] { "TenantId", "RecipientId", "ReportDate" },
                unique: true,
                filter: "\"Kind\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_DailyReportDeliveries_TenantId_Status_NextAttemptUtc",
                table: "DailyReportDeliveries",
                columns: new[] { "TenantId", "Status", "NextAttemptUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyReportRecipients_LinkCodeHash",
                table: "DailyReportRecipients",
                column: "LinkCodeHash");

            migrationBuilder.CreateIndex(
                name: "IX_DailyReportRecipients_TenantId",
                table: "DailyReportRecipients",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyReportRecipients_TenantId_IsActive",
                table: "DailyReportRecipients",
                columns: new[] { "TenantId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyReportDeliveries");

            migrationBuilder.DropTable(
                name: "DailyReportRecipients");

            migrationBuilder.DropColumn(
                name: "DailyReportAtLocal",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Headcount",
                table: "Locations");
        }
    }
}
