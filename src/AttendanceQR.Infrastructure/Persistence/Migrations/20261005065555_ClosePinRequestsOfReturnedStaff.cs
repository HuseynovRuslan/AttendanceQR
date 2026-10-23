using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceQR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClosePinRequestsOfReturnedStaff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data only. From now on a sign-in closes its own requests (PinResetQueue.CloseOnSignInAsync),
            // but the queue already holds the ones nobody answered: on 05.10.2026, 72 open, 71 of them from
            // people who had since scanned or opened the app on a working session. Managers are about to be
            // pushed to this screen, and resetting the PIN of somebody already back in signs them out — so
            // those close as Recovered (3) first. Whoever has done neither since asking stays open.
            migrationBuilder.Sql("""
                UPDATE "PinResetRequests" r
                SET "Status" = 3, "ResolvedAtUtc" = now()
                WHERE r."Status" = 0
                  AND (EXISTS (SELECT 1 FROM "AuditLogs" a
                               WHERE a."EmployeeId" = r."EmployeeId" AND a."EventType" IN (0, 2)
                                 AND a."CreatedAtUtc" > r."RequestedAtUtc")
                    OR EXISTS (SELECT 1 FROM "Employees" e
                               WHERE e."Id" = r."EmployeeId" AND e."LastActiveAtUtc" > r."RequestedAtUtc"));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: reopening a request for somebody who is already back in would only put the
            // wrong row back in front of a manager.
        }
    }
}
