namespace AttendanceQR.Api.Contracts;

/// <summary>Create/update a schedule template. Shift times are "HH:mm" strings (what the browser's
/// &lt;input type="time"&gt; emits). End earlier than start = overnight shift.</summary>
public record ScheduleRequest(
    string Name,
    string ShiftStart,
    string ShiftEnd,
    int LateThresholdMinutes = 15,
    int WorkDaysMask = 126);
