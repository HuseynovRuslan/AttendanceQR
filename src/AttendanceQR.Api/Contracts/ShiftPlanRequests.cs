namespace AttendanceQR.Api.Contracts;

/// <summary>
/// One change to the plan: these people, these days, these shifts.
///
/// The days are either <see cref="Dates"/> (the cells somebody clicked) or <see cref="From"/>–<see cref="To"/>.
/// <see cref="Steps"/> is what each day gets, in turn: a shift's id, <c>"rest"</c> for a day off, or <c>"clear"</c>
/// to drop the day's plan and fall back to the person's own shift. One step is «every day the same»; several are a
/// rotation, counted from <see cref="From"/> (2 day / 2 night / 2 off is six steps), so the pattern lands on the same
/// days whichever weekdays are kept.
/// </summary>
/// <param name="Weekdays">Only these weekdays (bit = .NET DayOfWeek, Sunday = bit 0). Null: every day.</param>
/// <param name="RestOnOtherDays">With <see cref="Weekdays"/>: the other days become rest days (true) or are left as
/// they are (false). «Monday to Friday, 09:00–18:00» means the weekend is off.</param>
/// <param name="ConfirmPast">A day before today changes a day already worked — its tabel and its pay — so it is only
/// written when the caller has said so, after being told how many such days there are.</param>
public record ShiftPlanApplyRequest(
    IReadOnlyList<Guid> EmployeeIds,
    IReadOnlyList<string> Steps,
    DateOnly? From = null,
    DateOnly? To = null,
    IReadOnlyList<DateOnly>? Dates = null,
    int? Weekdays = null,
    bool RestOnOtherDays = true,
    bool ConfirmPast = false,
    string? Note = null);
