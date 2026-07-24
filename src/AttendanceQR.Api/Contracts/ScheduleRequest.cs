namespace AttendanceQR.Api.Contracts;

/// <summary>Create/update a named shift ("növbə"). Shift times are "HH:mm" strings (what the
/// browser's &lt;input type="time"&gt; emits). End earlier than start = overnight shift.</summary>
public record ScheduleRequest(
    string Name,
    string ShiftStart,
    string ShiftEnd,
    int LateThresholdMinutes = 15,
    int WorkDaysMask = 126,
    // Rotation. Null WorkCycleDays = no rotation and WorkDaysMask decides. Two crews alternating on
    // the same pattern are two shifts anchored a day apart ("Gecə A", "Gecə B") — see Schedule.
    int? WorkCycleDays = null,
    int? WorkCycleOnDays = null,
    DateOnly? WorkCycleAnchor = null);
