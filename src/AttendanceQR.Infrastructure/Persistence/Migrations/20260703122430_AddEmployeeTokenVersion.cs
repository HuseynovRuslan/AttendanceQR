using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceQR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeTokenVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TokenVersion",
                table: "Employees",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TokenVersion",
                table: "Employees");
        }
    }
}
