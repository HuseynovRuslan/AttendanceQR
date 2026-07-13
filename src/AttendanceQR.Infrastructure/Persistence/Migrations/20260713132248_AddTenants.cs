using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceQR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "NonWorkingDays",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-00000000ba01"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "MissedCheckoutRequests",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-00000000ba01"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ManagedLocations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-00000000ba01"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Locations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-00000000ba01"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "LeaveRecords",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-00000000ba01"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Employees",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-00000000ba01"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "DeviceChangeRequests",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-00000000ba01"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "DeviceBindings",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-00000000ba01"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "DailySummaries",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-00000000ba01"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "AuditLogs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-00000000ba01"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "AttendanceRecords",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-00000000ba01"));

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LogoKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            // Seed the single pre-existing tenant (Bakı Abadlıq). All rows were backfilled to this id
            // via the column defaults above, so the foreign keys below validate. The default stays on
            // the columns for now (Phase 0 inserts don't set TenantId yet) — Phase 1 drops it once the
            // TenantId is stamped automatically from the request context.
            migrationBuilder.Sql(
                "INSERT INTO \"Tenants\" (\"Id\",\"Name\",\"Slug\",\"DisplayName\",\"IsActive\",\"CreatedAtUtc\") " +
                "VALUES ('00000000-0000-0000-0000-00000000ba01','Bakı Abadlıq','bax','Bakı Abadlıq',true, now()) " +
                "ON CONFLICT (\"Id\") DO NOTHING;");

            migrationBuilder.CreateIndex(
                name: "IX_NonWorkingDays_TenantId",
                table: "NonWorkingDays",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_MissedCheckoutRequests_TenantId",
                table: "MissedCheckoutRequests",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ManagedLocations_TenantId",
                table: "ManagedLocations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_TenantId",
                table: "Locations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRecords_TenantId",
                table: "LeaveRecords",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_TenantId",
                table: "Employees",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceChangeRequests_TenantId",
                table: "DeviceChangeRequests",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceBindings_TenantId",
                table: "DeviceBindings",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DailySummaries_TenantId",
                table: "DailySummaries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId",
                table: "AuditLogs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_TenantId",
                table: "AttendanceRecords",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Slug",
                table: "Tenants",
                column: "Slug",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceRecords_Tenants_TenantId",
                table: "AttendanceRecords",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AuditLogs_Tenants_TenantId",
                table: "AuditLogs",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DailySummaries_Tenants_TenantId",
                table: "DailySummaries",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DeviceBindings_Tenants_TenantId",
                table: "DeviceBindings",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DeviceChangeRequests_Tenants_TenantId",
                table: "DeviceChangeRequests",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Employees_Tenants_TenantId",
                table: "Employees",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LeaveRecords_Tenants_TenantId",
                table: "LeaveRecords",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Locations_Tenants_TenantId",
                table: "Locations",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ManagedLocations_Tenants_TenantId",
                table: "ManagedLocations",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MissedCheckoutRequests_Tenants_TenantId",
                table: "MissedCheckoutRequests",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_NonWorkingDays_Tenants_TenantId",
                table: "NonWorkingDays",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AttendanceRecords_Tenants_TenantId",
                table: "AttendanceRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_AuditLogs_Tenants_TenantId",
                table: "AuditLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_DailySummaries_Tenants_TenantId",
                table: "DailySummaries");

            migrationBuilder.DropForeignKey(
                name: "FK_DeviceBindings_Tenants_TenantId",
                table: "DeviceBindings");

            migrationBuilder.DropForeignKey(
                name: "FK_DeviceChangeRequests_Tenants_TenantId",
                table: "DeviceChangeRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_Employees_Tenants_TenantId",
                table: "Employees");

            migrationBuilder.DropForeignKey(
                name: "FK_LeaveRecords_Tenants_TenantId",
                table: "LeaveRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_Locations_Tenants_TenantId",
                table: "Locations");

            migrationBuilder.DropForeignKey(
                name: "FK_ManagedLocations_Tenants_TenantId",
                table: "ManagedLocations");

            migrationBuilder.DropForeignKey(
                name: "FK_MissedCheckoutRequests_Tenants_TenantId",
                table: "MissedCheckoutRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_NonWorkingDays_Tenants_TenantId",
                table: "NonWorkingDays");

            migrationBuilder.DropTable(
                name: "Tenants");

            migrationBuilder.DropIndex(
                name: "IX_NonWorkingDays_TenantId",
                table: "NonWorkingDays");

            migrationBuilder.DropIndex(
                name: "IX_MissedCheckoutRequests_TenantId",
                table: "MissedCheckoutRequests");

            migrationBuilder.DropIndex(
                name: "IX_ManagedLocations_TenantId",
                table: "ManagedLocations");

            migrationBuilder.DropIndex(
                name: "IX_Locations_TenantId",
                table: "Locations");

            migrationBuilder.DropIndex(
                name: "IX_LeaveRecords_TenantId",
                table: "LeaveRecords");

            migrationBuilder.DropIndex(
                name: "IX_Employees_TenantId",
                table: "Employees");

            migrationBuilder.DropIndex(
                name: "IX_DeviceChangeRequests_TenantId",
                table: "DeviceChangeRequests");

            migrationBuilder.DropIndex(
                name: "IX_DeviceBindings_TenantId",
                table: "DeviceBindings");

            migrationBuilder.DropIndex(
                name: "IX_DailySummaries_TenantId",
                table: "DailySummaries");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_TenantId",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceRecords_TenantId",
                table: "AttendanceRecords");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "NonWorkingDays");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "MissedCheckoutRequests");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ManagedLocations");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "LeaveRecords");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "DeviceChangeRequests");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "DeviceBindings");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "DailySummaries");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AttendanceRecords");
        }
    }
}
