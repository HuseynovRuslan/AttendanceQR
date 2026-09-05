using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceQR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFieldVisitReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReviewNote",
                table: "FieldVisits",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ReviewOk",
                table: "FieldVisits",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAtUtc",
                table: "FieldVisits",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewedByEmployeeId",
                table: "FieldVisits",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReviewNote",
                table: "FieldVisits");

            migrationBuilder.DropColumn(
                name: "ReviewOk",
                table: "FieldVisits");

            migrationBuilder.DropColumn(
                name: "ReviewedAtUtc",
                table: "FieldVisits");

            migrationBuilder.DropColumn(
                name: "ReviewedByEmployeeId",
                table: "FieldVisits");
        }
    }
}
