using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceQR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccessRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AuthzMode",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Locations_TenantId_Id",
                table: "Locations",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateTable(
                name: "AccessRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TemplateKey = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    PeersManageable = table.Column<bool>(type: "boolean", nullable: false),
                    Permissions = table.Column<List<string>>(type: "text[]", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessRoles", x => x.Id);
                    table.UniqueConstraint("AK_AccessRoles_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_AccessRoles_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuthzShadowEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Legacy = table.Column<bool>(type: "boolean", nullable: false),
                    New = table.Column<bool>(type: "boolean", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false),
                    FirstAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthzShadowEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthzShadowEvents_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RoleAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessRoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<int>(type: "integer", nullable: false),
                    StructureId = table.Column<Guid>(type: "uuid", nullable: true),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleAssignments", x => x.Id);
                    table.CheckConstraint("CK_RoleAssignments_Scope", "(\"Scope\" = 0 AND \"StructureId\" IS NULL AND \"LocationId\" IS NULL) OR (\"Scope\" = 1 AND \"StructureId\" IS NOT NULL AND \"LocationId\" IS NULL) OR (\"Scope\" = 2 AND \"LocationId\" IS NOT NULL AND \"StructureId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_RoleAssignments_AccessRoles_TenantId_AccessRoleId",
                        columns: x => new { x.TenantId, x.AccessRoleId },
                        principalTable: "AccessRoles",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoleAssignments_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoleAssignments_Locations_TenantId_LocationId",
                        columns: x => new { x.TenantId, x.LocationId },
                        principalTable: "Locations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoleAssignments_Structures_TenantId_StructureId",
                        columns: x => new { x.TenantId, x.StructureId },
                        principalTable: "Structures",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoleAssignments_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessRoles_TenantId",
                table: "AccessRoles",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessRoles_TenantId_Name",
                table: "AccessRoles",
                columns: new[] { "TenantId", "Name" },
                unique: true,
                filter: "\"ArchivedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AccessRoles_TenantId_TemplateKey",
                table: "AccessRoles",
                columns: new[] { "TenantId", "TemplateKey" },
                unique: true,
                filter: "\"TemplateKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AuthzShadowEvents_TenantId",
                table: "AuthzShadowEvents",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AuthzShadowEvents_TenantId_Day_Kind_Subject_EmployeeId_Targ~",
                table: "AuthzShadowEvents",
                columns: new[] { "TenantId", "Day", "Kind", "Subject", "EmployeeId", "TargetKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoleAssignments_EmployeeId_AccessRoleId_Scope_StructureId_L~",
                table: "RoleAssignments",
                columns: new[] { "EmployeeId", "AccessRoleId", "Scope", "StructureId", "LocationId" },
                unique: true,
                filter: "\"RevokedAtUtc\" IS NULL")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_RoleAssignments_TenantId",
                table: "RoleAssignments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_RoleAssignments_TenantId_AccessRoleId",
                table: "RoleAssignments",
                columns: new[] { "TenantId", "AccessRoleId" });

            migrationBuilder.CreateIndex(
                name: "IX_RoleAssignments_TenantId_EmployeeId",
                table: "RoleAssignments",
                columns: new[] { "TenantId", "EmployeeId" },
                filter: "\"RevokedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RoleAssignments_TenantId_LocationId",
                table: "RoleAssignments",
                columns: new[] { "TenantId", "LocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_RoleAssignments_TenantId_StructureId",
                table: "RoleAssignments",
                columns: new[] { "TenantId", "StructureId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthzShadowEvents");

            migrationBuilder.DropTable(
                name: "RoleAssignments");

            migrationBuilder.DropTable(
                name: "AccessRoles");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Locations_TenantId_Id",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "AuthzMode",
                table: "Tenants");
        }
    }
}
