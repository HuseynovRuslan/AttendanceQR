using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceQR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovalRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApprovalAreas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<int>(type: "integer", nullable: false),
                    StructureId = table.Column<Guid>(type: "uuid", nullable: true),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActivatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActivatedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeactivatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeactivatedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalAreas", x => x.Id);
                    table.CheckConstraint("CK_ApprovalAreas_Scope", "(\"Scope\" = 1 AND \"StructureId\" IS NOT NULL AND \"LocationId\" IS NULL) OR (\"Scope\" = 2 AND \"LocationId\" IS NOT NULL AND \"StructureId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_ApprovalAreas_Locations_TenantId_LocationId",
                        columns: x => new { x.TenantId, x.LocationId },
                        principalTable: "Locations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApprovalAreas_Structures_TenantId_StructureId",
                        columns: x => new { x.TenantId, x.StructureId },
                        principalTable: "Structures",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApprovalAreas_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ApproverSubstitutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    StructureId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubstituteEmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApproverSubstitutions", x => x.Id);
                    table.CheckConstraint("CK_ApproverSubstitutions_Period", "\"EndsAtUtc\" > \"StartsAtUtc\"");
                    table.ForeignKey(
                        name: "FK_ApproverSubstitutions_Employees_SubstituteEmployeeId",
                        column: x => x.SubstituteEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApproverSubstitutions_Structures_TenantId_StructureId",
                        columns: x => new { x.TenantId, x.StructureId },
                        principalTable: "Structures",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApproverSubstitutions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChangeRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Direct = table.Column<bool>(type: "boolean", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    StructureIdAtCreation = table.Column<Guid>(type: "uuid", nullable: true),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DedupeKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RequestedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DecidedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecisionNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AppliedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NotifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RemindedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChangeRequests", x => x.Id);
                    table.UniqueConstraint("AK_ChangeRequests_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_ChangeRequests_Decision", "(\"Status\" = 0 AND \"DecidedAtUtc\" IS NULL AND \"DecidedByEmployeeId\" IS NULL AND \"AppliedAtUtc\" IS NULL) OR (\"Status\" IN (2, 3) AND \"DecidedAtUtc\" IS NOT NULL AND \"DecidedByEmployeeId\" IS NOT NULL AND \"AppliedAtUtc\" IS NULL) OR (\"Status\" = 1 AND \"DecidedAtUtc\" IS NOT NULL AND \"DecidedByEmployeeId\" IS NOT NULL AND \"AppliedAtUtc\" IS NOT NULL)");
                    table.CheckConstraint("CK_ChangeRequests_Direct", "NOT \"Direct\" OR \"Status\" = 1");
                    table.CheckConstraint("CK_ChangeRequests_Number", "\"Direct\" OR \"Number\" IS NOT NULL");
                    table.CheckConstraint("CK_ChangeRequests_RejectReason", "\"Status\" <> 2 OR length(btrim(coalesce(\"DecisionNote\", ''))) > 0");
                    table.CheckConstraint("CK_ChangeRequests_Status", "\"Status\" IN (0, 1, 2, 3)");
                    table.ForeignKey(
                        name: "FK_ChangeRequests_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ChangeRequests_Locations_TenantId_LocationId",
                        columns: x => new { x.TenantId, x.LocationId },
                        principalTable: "Locations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChangeRequests_Structures_TenantId_StructureIdAtCreation",
                        columns: x => new { x.TenantId, x.StructureIdAtCreation },
                        principalTable: "Structures",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChangeRequests_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChangeRequestEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangeRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    ActorEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Data = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChangeRequestEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChangeRequestEvents_ChangeRequests_TenantId_ChangeRequestId",
                        columns: x => new { x.TenantId, x.ChangeRequestId },
                        principalTable: "ChangeRequests",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChangeRequestEvents_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalAreas_TenantId",
                table: "ApprovalAreas",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalAreas_TenantId_LocationId",
                table: "ApprovalAreas",
                columns: new[] { "TenantId", "LocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalAreas_TenantId_Scope_StructureId_LocationId",
                table: "ApprovalAreas",
                columns: new[] { "TenantId", "Scope", "StructureId", "LocationId" },
                unique: true,
                filter: "\"DeactivatedAtUtc\" IS NULL")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalAreas_TenantId_StructureId",
                table: "ApprovalAreas",
                columns: new[] { "TenantId", "StructureId" });

            migrationBuilder.CreateIndex(
                name: "IX_ApproverSubstitutions_SubstituteEmployeeId",
                table: "ApproverSubstitutions",
                column: "SubstituteEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_ApproverSubstitutions_TenantId",
                table: "ApproverSubstitutions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ApproverSubstitutions_TenantId_StructureId_EndsAtUtc",
                table: "ApproverSubstitutions",
                columns: new[] { "TenantId", "StructureId", "EndsAtUtc" },
                filter: "\"RevokedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequestEvents_OneDecision",
                table: "ChangeRequestEvents",
                column: "ChangeRequestId",
                unique: true,
                filter: "\"Kind\" IN (1, 2, 3, 6)");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequestEvents_TenantId",
                table: "ChangeRequestEvents",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequestEvents_TenantId_ChangeRequestId_AtUtc",
                table: "ChangeRequestEvents",
                columns: new[] { "TenantId", "ChangeRequestId", "AtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequests_EmployeeId",
                table: "ChangeRequests",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequests_OnePending",
                table: "ChangeRequests",
                columns: new[] { "TenantId", "Type", "DedupeKey" },
                unique: true,
                filter: "\"Status\" = 0 AND \"DedupeKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequests_TenantId",
                table: "ChangeRequests",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequests_TenantId_EmployeeId",
                table: "ChangeRequests",
                columns: new[] { "TenantId", "EmployeeId" },
                filter: "\"EmployeeId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequests_TenantId_IdempotencyKey",
                table: "ChangeRequests",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequests_TenantId_LocationId",
                table: "ChangeRequests",
                columns: new[] { "TenantId", "LocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequests_TenantId_Number",
                table: "ChangeRequests",
                columns: new[] { "TenantId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequests_TenantId_RequestedByEmployeeId_CreatedAtUtc",
                table: "ChangeRequests",
                columns: new[] { "TenantId", "RequestedByEmployeeId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequests_TenantId_Status_LocationId_CreatedAtUtc",
                table: "ChangeRequests",
                columns: new[] { "TenantId", "Status", "LocationId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequests_TenantId_StructureIdAtCreation",
                table: "ChangeRequests",
                columns: new[] { "TenantId", "StructureIdAtCreation" });

            // A request's history is append-only. The code only ever inserts lines; this makes the database
            // refuse an UPDATE as well, so a line once written says what happened, whoever touches the table.
            // DELETE stays possible: the only delete is the tenant purge (by cascade from the request).
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION "ChangeRequestEvents_AppendOnly"() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'ChangeRequestEvents is append-only' USING ERRCODE = 'restrict_violation';
                END;
                $$;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "trg_ChangeRequestEvents_AppendOnly"
                BEFORE UPDATE ON "ChangeRequestEvents"
                FOR EACH ROW EXECUTE FUNCTION "ChangeRequestEvents_AppendOnly"();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "trg_ChangeRequestEvents_AppendOnly" ON "ChangeRequestEvents";""");
            migrationBuilder.Sql("""DROP FUNCTION IF EXISTS "ChangeRequestEvents_AppendOnly"();""");

            migrationBuilder.DropTable(
                name: "ApprovalAreas");

            migrationBuilder.DropTable(
                name: "ApproverSubstitutions");

            migrationBuilder.DropTable(
                name: "ChangeRequestEvents");

            migrationBuilder.DropTable(
                name: "ChangeRequests");
        }
    }
}
