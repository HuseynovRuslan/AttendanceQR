namespace AttendanceQR.Api.Contracts;

/// <summary>Create/update a named shift ("növbə"). Shift times are "HH:mm" strings (what the
/// browser's &lt;input type="time"&gt; emits). End earlier than start = overnight shift.</summary>
public record ScheduleRequest(
    string Name,
    string ShiftStart,
    string ShiftEnd,
    // The branch this shift belongs to, or null for one the whole company shares. See Schedule.
    Guid? LocationId = null,
    int LateThresholdMinutes = 15,
    int WorkDaysMask = 126,
    // Rotation. Null WorkCycleDays = no rotation and WorkDaysMask decides. Two crews alternating on
    // the same pattern are two shifts anchored a day apart ("Gecə A", "Gecə B") — see Schedule.
    int? WorkCycleDays = null,
    int? WorkCycleOnDays = null,
    DateOnly? WorkCycleAnchor = null,
    // Days whose hours differ from ShiftStart/ShiftEnd, keyed by day number (Sunday=0 … Saturday=6)
    // with "HH:mm" strings: {"6": {"start": "09:00", "end": "18:00"}}. Absent days keep the shift's
    // ordinary hours. Null or empty clears every override. See AttendanceQR.Application DayHours.
    Dictionary<string, DayHoursRequest>? DayHours = null);

/// <summary>One day's own hours, in the same "HH:mm" the shift itself uses.</summary>
public record DayHoursRequest(string Start, string End);
