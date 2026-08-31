using AttendanceQR.Api.Contracts;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;

namespace AttendanceQR.Api;

/// <summary>
/// Turning the request's per-day hours into the column the schedule stores.
///
/// Shared by the admin and the manager endpoints for the reason the rest of this codebase keeps
/// learning: two copies of a rule are two chances for a shift to mean one thing when an admin saves
/// it and another when a manager does, and the rule here decides which clock somebody's day is
/// measured against.
/// </summary>
public static class ScheduleDayHours
{
    /// <summary>Writes the overrides onto the schedule, or an error message if any of them is junk.</summary>
    public static string? Apply(Schedule schedule, Dictionary<string, DayHoursRequest>? requested)
    {
        if (requested is null || requested.Count == 0)
        {
            // An empty map CLEARS them. Anything else would make an override impossible to remove
            // through the same form that created it.
            schedule.DayHours = null;
            return null;
        }

        var map = new Dictionary<DayOfWeek, (TimeOnly Start, TimeOnly End)>();
        foreach (var (key, value) in requested)
        {
            if (!int.TryParse(key, out var day) || day is < 0 or > 6) return "InvalidDayHours";
            if (!TimeOnly.TryParse(value.Start, out var start)) return "InvalidDayHours";
            if (!TimeOnly.TryParse(value.End, out var end)) return "InvalidDayHours";
            if (start == end) return "InvalidDayHours";
            map[(DayOfWeek)day] = (start, end);
        }

        schedule.DayHours = DayHours.Format(map);
        return null;
    }
}
