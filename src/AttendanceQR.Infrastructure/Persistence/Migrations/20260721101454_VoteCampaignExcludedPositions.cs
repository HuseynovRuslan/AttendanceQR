using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceQR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VoteCampaignExcludedPositions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "ExcludedPositions",
                table: "VoteCampaigns",
                type: "text[]",
                nullable: false,
                // Existing campaigns exclude nobody. Without a default the column cannot be added to
                // a table that already has rows.
                defaultValueSql: "'{}'::text[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExcludedPositions",
                table: "VoteCampaigns");
        }
    }
}
