using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceQR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantTrialEnd : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "TrialEndsAtUtc",
                table: "Tenants",
                type: "timestamp with time zone",
                nullable: true);

            // Every company on the system today is on a demo until 31 August 2026 (owner's date).
            // Stamping it here rather than leaving the column null is what makes the new billing
            // screen tell them the truth on the very first load — a null would have read as "ordinary
            // paying subscription", which is not what any of them agreed to. A tenant created after
            // this runs gets null and is priced from day one, unless the operator sets a demo when
            // they create it; extending any of these is a date field on the operator's plan form.
            migrationBuilder.Sql(
                @"UPDATE ""Tenants"" SET ""TrialEndsAtUtc"" = TIMESTAMPTZ '2026-08-31 00:00:00+00' WHERE ""TrialEndsAtUtc"" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TrialEndsAtUtc",
                table: "Tenants");
        }
    }
}
