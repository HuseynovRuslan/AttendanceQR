using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceQR.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// «Elastik qrafik» (Schedule.IsFlexible) and its optional hour norm. Purely additive: every existing shift reads
    /// IsFlexible = false (fixed hours, exactly as before), no norm, period Day. Nothing is recomputed by this migration.
    /// </summary>
    public partial class AddFlexibleSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FlexibleNormMinutes",
                table: "Schedules",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FlexibleNormPeriod",
                table: "Schedules",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsFlexible",
                table: "Schedules",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FlexibleNormMinutes",
                table: "Schedules");

            migrationBuilder.DropColumn(
                name: "FlexibleNormPeriod",
                table: "Schedules");

            migrationBuilder.DropColumn(
                name: "IsFlexible",
                table: "Schedules");
        }
    }
}
