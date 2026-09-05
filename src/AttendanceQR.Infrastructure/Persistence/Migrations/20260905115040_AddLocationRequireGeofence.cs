using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceQR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationRequireGeofence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // TRUE, not the CLR default EF generates. Every branch that already exists has a fence
            // and must keep it: a false here would have taken the geofence down at all 22 sites in one
            // deploy, silently, and the only gate on a check-in with it. The property initializer says
            // true for NEW branches; this says the same for the ones already in the table.
            migrationBuilder.AddColumn<bool>(
                name: "RequireGeofence",
                table: "Locations",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequireGeofence",
                table: "Locations");
        }
    }
}
