using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceQR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStructures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "StructureId",
                table: "Locations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Structures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Structures", x => x.Id);
                    table.UniqueConstraint("AK_Structures_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Structures_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Locations_TenantId_StructureId",
                table: "Locations",
                columns: new[] { "TenantId", "StructureId" });

            migrationBuilder.CreateIndex(
                name: "IX_Structures_TenantId",
                table: "Structures",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Structures_TenantId_Name",
                table: "Structures",
                columns: new[] { "TenantId", "Name" },
                unique: true,
                filter: "\"ArchivedAtUtc\" IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Locations_Structures_TenantId_StructureId",
                table: "Locations",
                columns: new[] { "TenantId", "StructureId" },
                principalTable: "Structures",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Locations_Structures_TenantId_StructureId",
                table: "Locations");

            migrationBuilder.DropTable(
                name: "Structures");

            migrationBuilder.DropIndex(
                name: "IX_Locations_TenantId_StructureId",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "StructureId",
                table: "Locations");
        }
    }
}
