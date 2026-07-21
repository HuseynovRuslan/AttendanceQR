using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceQR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VoteCampaignTimes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeOnly>(
                name: "EndsAt",
                table: "VoteCampaigns",
                type: "time without time zone",
                nullable: false,
                // Existing campaigns ran on whole days: 00:00 here would close them at the START of
                // their last day, i.e. retroactively shut a ballot that was open.
                defaultValue: new TimeOnly(23, 59, 0));

            migrationBuilder.AddColumn<TimeOnly>(
                name: "StartsAt",
                table: "VoteCampaigns",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EndsAt",
                table: "VoteCampaigns");

            migrationBuilder.DropColumn(
                name: "StartsAt",
                table: "VoteCampaigns");
        }
    }
}
