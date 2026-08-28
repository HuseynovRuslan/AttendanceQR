using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceQR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCanShareDevice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CanShareDevice",
                table: "Employees",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Anyone ALREADY on somebody else's device keeps the arrangement they have. The column
            // defaults to false because that is the right default for the other 900-odd rows, but
            // applying it to the brigades that are already working this way would take a permission
            // away from people who never had to ask for it — and they would find out by standing at a
            // poster unable to clock in. Turning it on here changes nothing about how they scan today;
            // it only records the state that already exists.
            migrationBuilder.Sql(
                @"UPDATE ""Employees"" e SET ""CanShareDevice"" = true
                  WHERE EXISTS (
                      SELECT 1
                      FROM ""DeviceBindings"" a
                      JOIN ""DeviceBindings"" b
                        ON b.""DeviceFingerprint"" = a.""DeviceFingerprint""
                       AND b.""EmployeeId"" <> a.""EmployeeId""
                       AND b.""RevokedAtUtc"" IS NULL
                      WHERE a.""EmployeeId"" = e.""Id"" AND a.""RevokedAtUtc"" IS NULL);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanShareDevice",
                table: "Employees");
        }
    }
}
