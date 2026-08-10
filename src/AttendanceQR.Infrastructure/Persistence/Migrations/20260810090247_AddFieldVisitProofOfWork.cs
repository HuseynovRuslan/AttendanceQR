using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceQR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFieldVisitProofOfWork : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "WorkPhotoAtUtc",
                table: "FieldVisits",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkPhotoKey",
                table: "FieldVisits",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FieldVisitChecklistItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FieldVisitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsDone = table.Column<bool>(type: "boolean", nullable: false),
                    DoneAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FieldVisitChecklistItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FieldVisitChecklistItems_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FieldVisitChecklistItems_TenantId",
                table: "FieldVisitChecklistItems",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_FieldVisitChecklistItems_TenantId_FieldVisitId",
                table: "FieldVisitChecklistItems",
                columns: new[] { "TenantId", "FieldVisitId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FieldVisitChecklistItems");

            migrationBuilder.DropColumn(
                name: "WorkPhotoAtUtc",
                table: "FieldVisits");

            migrationBuilder.DropColumn(
                name: "WorkPhotoKey",
                table: "FieldVisits");
        }
    }
}
