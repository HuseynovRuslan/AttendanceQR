namespace AttendanceQR.Api;

/// <summary>
/// Whether a person may be put on a shift.
///
/// One line of logic, in its own file for the same reason <see cref="TemporaryPinGate"/> is: it is
/// decided in two controllers (an admin edits anyone, a manager edits their own staff), and a rule
/// duplicated in two places is a rule that will disagree with itself. It is also the only thing
/// standing between an operator and the mistake this feature exists to prevent — putting one branch's
/// crew shift on somebody who works somewhere else, which produces no error and no flag, just hours
/// that quietly do not match the work and a person marked late by a schedule they were never on.
/// </summary>
public static class ScheduleAssignmentRule
{
    /// <summary>
    /// The error code to refuse with, or null when the assignment is fine.
    ///
    /// A shift with no branch (<paramref name="scheduleLocationId"/> null) belongs to the whole
    /// company and fits anybody — which is what every shift created before branches could own one is,
    /// so nothing that exists today is affected.
    /// </summary>
    public static string? Refusal(Guid? scheduleLocationId, Guid employeeLocationId)
        => scheduleLocationId is Guid pinned && pinned != employeeLocationId
            ? "ScheduleBelongsToOtherBranch"
            : null;
}
