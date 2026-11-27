using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceQR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NotificationBranchPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationBranchPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationBranchPreferences_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NotificationBranchPreferences_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NotificationBranchPreferences_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubjectEmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttendanceRecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    WasOffline = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationEvents_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EnabledTypes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    BranchMode = table.Column<int>(type: "integer", nullable: false),
                    InApp = table.Column<bool>(type: "boolean", nullable: false),
                    Push = table.Column<bool>(type: "boolean", nullable: false),
                    Web = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationPreferences_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NotificationPreferences_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientEmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: true),
                    TypeKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Body = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    SubjectEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubjectName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    BranchName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    WasOffline = table.Column<bool>(type: "boolean", nullable: false),
                    Minutes = table.Column<int>(type: "integer", nullable: true),
                    Url = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_Employees_RecipientEmployeeId",
                        column: x => x.RecipientEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Notifications_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecipientEmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    TypeKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Body = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Url = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationDeliveries_Employees_RecipientEmployeeId",
                        column: x => x.RecipientEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NotificationDeliveries_Notifications_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "Notifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NotificationDeliveries_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PushSubscriptions_TenantId_EmployeeId",
                table: "PushSubscriptions",
                columns: new[] { "TenantId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationBranchPreferences_EmployeeId",
                table: "NotificationBranchPreferences",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationBranchPreferences_LocationId",
                table: "NotificationBranchPreferences",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationBranchPreferences_TenantId",
                table: "NotificationBranchPreferences",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationBranchPreferences_TenantId_EmployeeId_Category_~",
                table: "NotificationBranchPreferences",
                columns: new[] { "TenantId", "EmployeeId", "Category", "LocationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_NotificationId",
                table: "NotificationDeliveries",
                column: "NotificationId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_RecipientEmployeeId",
                table: "NotificationDeliveries",
                column: "RecipientEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_TenantId",
                table: "NotificationDeliveries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_TenantId_Channel_Status_NextAttemptU~",
                table: "NotificationDeliveries",
                columns: new[] { "TenantId", "Channel", "Status", "NextAttemptUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_TenantId_RecipientEmployeeId_SentAtU~",
                table: "NotificationDeliveries",
                columns: new[] { "TenantId", "RecipientEmployeeId", "SentAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationEvents_TenantId",
                table: "NotificationEvents",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationEvents_TenantId_Status_NextAttemptUtc",
                table: "NotificationEvents",
                columns: new[] { "TenantId", "Status", "NextAttemptUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPreferences_EmployeeId",
                table: "NotificationPreferences",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPreferences_TenantId",
                table: "NotificationPreferences",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPreferences_TenantId_Category",
                table: "NotificationPreferences",
                columns: new[] { "TenantId", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPreferences_TenantId_EmployeeId_Category",
                table: "NotificationPreferences",
                columns: new[] { "TenantId", "EmployeeId", "Category" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_EventId_RecipientEmployeeId_TypeKey",
                table: "Notifications",
                columns: new[] { "EventId", "RecipientEmployeeId", "TypeKey" },
                unique: true,
                filter: "\"EventId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientEmployeeId",
                table: "Notifications",
                column: "RecipientEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_TenantId",
                table: "Notifications",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_TenantId_RecipientEmployeeId_CreatedAtUtc",
                table: "Notifications",
                columns: new[] { "TenantId", "RecipientEmployeeId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Unread",
                table: "Notifications",
                columns: new[] { "TenantId", "RecipientEmployeeId" },
                filter: "\"ReadAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationBranchPreferences");

            migrationBuilder.DropTable(
                name: "NotificationDeliveries");

            migrationBuilder.DropTable(
                name: "NotificationEvents");

            migrationBuilder.DropTable(
                name: "NotificationPreferences");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_PushSubscriptions_TenantId_EmployeeId",
                table: "PushSubscriptions");
        }
    }
}
