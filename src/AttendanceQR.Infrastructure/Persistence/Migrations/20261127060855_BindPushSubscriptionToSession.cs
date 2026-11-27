using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceQR.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BindPushSubscriptionToSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // -1 (PushSubscription.Unbound), NOT 0: every row that exists today was registered by a session
            // nobody recorded. A 0 would read as "registered at TokenVersion 0" and silently stop every
            // subscription whose employee is past version 0 — nobody would get the check-out reminder, and
            // nothing would say why. -1 means "adopt me": PushSubscriptionAdoption stamps these rows with the
            // employee's current version once at startup (and PushSessionBinding does it at send time for any
            // it still meets). Additive only — no raw SQL, which the release helper refuses to apply.
            migrationBuilder.AddColumn<int>(
                name: "TokenVersion",
                table: "PushSubscriptions",
                type: "integer",
                nullable: false,
                defaultValue: -1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TokenVersion",
                table: "PushSubscriptions");
        }
    }
}
