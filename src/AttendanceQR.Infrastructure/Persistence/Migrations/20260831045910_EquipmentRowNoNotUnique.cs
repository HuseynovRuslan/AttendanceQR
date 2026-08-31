using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceQR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EquipmentRowNoNotUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EquipmentRecords_TenantId_RowNo",
                table: "EquipmentRecords");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentRecords_TenantId_RowNo",
                table: "EquipmentRecords",
                columns: new[] { "TenantId", "RowNo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EquipmentRecords_TenantId_RowNo",
                table: "EquipmentRecords");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentRecords_TenantId_RowNo",
                table: "EquipmentRecords",
                columns: new[] { "TenantId", "RowNo" },
                unique: true);
        }
    }
}
