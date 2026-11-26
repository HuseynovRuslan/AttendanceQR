using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api;

/// <summary>
/// What deleting a person or a branch does with each row that would otherwise hold it in place.
///
/// <see cref="RowReferences.Holding"/> finds those rows from the model; this says what each one means.
/// A reference that is in the model but not here is not guessed at: the delete is refused with a 409 that
/// names the table (never a 500), and <c>DeleteRulesTests</c> fails until someone decides. That pair is
/// the point — the employee delete used to know three tables, role assignments and PIN-reset requests
/// arrived later, and every delete of a former manager or of anyone who had pressed «PIN-i unutdum»
/// failed in production while every test stayed green.
/// </summary>
public static class DeleteRules
{
    /// <summary>What the admin is told the person's history consists of, when there is some.</summary>
    public enum Kind
    {
        /// <summary>Scans, the daily summaries built from them, the missed check-outs filed against them.</summary>
        Attendance,
        /// <summary>New-phone requests they made, or reviewed for somebody else.</summary>
        Requests,
    }

    /// <summary>What happens to the row when the person goes.</summary>
    public enum Action
    {
        /// <summary>The row is this person's own and goes with them.</summary>
        Remove,
        /// <summary>The row is someone else's that merely names this person (as reviewer, as resolver). It
        /// stays; the name is cleared — the same as AuditLogs, which the database sets null.</summary>
        Detach,
    }

    /// <param name="Action">What a delete does with the row.</param>
    /// <param name="History">Null: the row simply goes with the person (or loses their name), no question
    /// asked. Otherwise it is history somebody may still ask about — its presence makes the delete need the
    /// explicit «TAM silinsin» confirmation (force), and the admin is told it is this kind.</param>
    public sealed record Rule(Action Action, Kind? History);

    /// <summary>
    /// Every reference that holds an <see cref="Domain.Entities.Employee"/> row. All of it — history or
    /// not — is removed or anonymised in the same SaveChanges as the person: one transaction, so a refusal
    /// half-way can no longer leave the person standing with their past days gone.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Rule> Employee = new Dictionary<string, Rule>(StringComparer.Ordinal)
    {
        ["AttendanceRecord.EmployeeId"] = new(Action.Remove, Kind.Attendance),
        ["DailySummary.EmployeeId"] = new(Action.Remove, Kind.Attendance),
        // Exists only beside an attendance row of the same person (and cascades with it); listed because the
        // key is Restrict in its own right.
        ["MissedCheckoutRequest.EmployeeId"] = new(Action.Remove, Kind.Attendance),
        ["DeviceChangeRequest.EmployeeId"] = new(Action.Remove, Kind.Requests),
        ["DeviceChangeRequest.ReviewedByEmployeeId"] = new(Action.Detach, Kind.Requests),
        // Not history, so not behind the confirmation. «Revoked, never deleted» is the rule for taking a role
        // away; a person deleted outright cannot keep rows that name them, and what they did stays in
        // AuditLogs, unnamed. Every former manager failed to delete on these rows (the access migration gave
        // each manager role rows, and a demotion revokes them but keeps them).
        ["RoleAssignment.EmployeeId"] = new(Action.Remove, null),
        ["PinResetRequest.EmployeeId"] = new(Action.Remove, null),
        // A request they only resolved is someone else's: the row stays, the resolver is cleared.
        ["PinResetRequest.ResolvedByEmployeeId"] = new(Action.Detach, null),
    };

    /// <summary>
    /// Every reference that holds a <see cref="Domain.Entities.Location"/> row and that the branch delete
    /// answers for itself — staff (move them), scans (deactivate instead), daily summaries (they follow the
    /// employee), role grants (the system's mirrors of «manages this branch» go with it, a person's grant
    /// is a record), approval areas and requests (records).
    /// </summary>
    public static readonly IReadOnlySet<string> Location = new HashSet<string>(StringComparer.Ordinal)
    {
        "Employee.LocationId",
        "AttendanceRecord.LocationId",
        "DailySummary.LocationId",
        "RoleAssignment.LocationId",
        "ApprovalArea.LocationId",
        "ChangeRequest.LocationId",
    };

    /// <summary>A foreign-key refusal from Postgres (23503), with the table that still holds the row.</summary>
    public static bool IsForeignKeyRefusal(DbUpdateException ex, out string? table)
    {
        if (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.ForeignKeyViolation } pg)
        {
            table = pg.TableName;
            return true;
        }
        table = null;
        return false;
    }
}
