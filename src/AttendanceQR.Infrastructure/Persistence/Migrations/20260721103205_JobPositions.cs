using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceQR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class JobPositions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JobPositions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobPositions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobPositions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobPositions_TenantId",
                table: "JobPositions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_JobPositions_TenantId_Name",
                table: "JobPositions",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            // Seed the catalogue from the titles employees already hold, per tenant. Starting it empty
            // would mean every existing employee's position was suddenly "not in the list".
            migrationBuilder.Sql("""
                INSERT INTO "JobPositions" ("Id", "TenantId", "Name", "CreatedAtUtc")
                SELECT gen_random_uuid(), "TenantId", btrim("Position"), NOW() AT TIME ZONE 'utc'
                FROM "Employees"
                WHERE "Position" IS NOT NULL AND btrim("Position") <> ''
                GROUP BY "TenantId", btrim("Position");
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobPositions");
        }
    }
}
