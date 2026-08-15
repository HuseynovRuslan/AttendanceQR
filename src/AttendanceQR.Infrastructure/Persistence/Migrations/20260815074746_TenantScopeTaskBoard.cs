using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceQR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TenantScopeTaskBoard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Tasks",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Backfill, or every existing task becomes invisible the instant the query filter turns
            // on: a Guid.Empty TenantId matches no company, so the board would look wiped rather than
            // moved. Each task goes to the company of whoever created it — which, for every row that
            // exists, is the one person who has ever used this board.
            migrationBuilder.Sql("""
                UPDATE "Tasks" t
                SET "TenantId" = e."TenantId"
                FROM "Employees" e
                WHERE e."Id" = t."CreatedByEmployeeId"
                  AND t."TenantId" = '00000000-0000-0000-0000-000000000000';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Tasks");
        }
    }
}
