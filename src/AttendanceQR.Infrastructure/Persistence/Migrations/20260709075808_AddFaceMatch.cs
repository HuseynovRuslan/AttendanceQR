using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceQR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFaceMatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FaceMatchScore",
                table: "AttendanceRecords",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FaceMatchStatus",
                table: "AttendanceRecords",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FaceMatchScore",
                table: "AttendanceRecords");

            migrationBuilder.DropColumn(
                name: "FaceMatchStatus",
                table: "AttendanceRecords");
        }
    }
}
