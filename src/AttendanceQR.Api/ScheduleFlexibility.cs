using AttendanceQR.Api.Contracts;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;

namespace AttendanceQR.Api;

/// <summary>
/// «Elastik qrafik» on the shift forms — the admin's and the manager's twin share it, so the two routes into the data
/// can never accept a flexible shift the other would reject (the same reason WorkCycle.Apply is shared).
///
/// The request's flexible fields are OPTIONAL and null means «unchanged»: ScheduleRequest is otherwise a full
/// replace, and a client that has never heard of the mode — the manager's form, an old cached app — must not switch a
/// company's flexible shift back to fixed hours just by saving its name.
/// </summary>
public static class ScheduleFlexibility
{
    /// <summary>Applies the request's mode and norm to <paramref name="schedule"/>. Null on success, else an error code.
    /// Call it AFTER the hours, rotation and per-day hours are applied: a flexible shift clears them.</summary>
    public static string? Apply(Schedule schedule, ScheduleRequest request)
    {
        if (request.IsFlexible is not bool flexible)
            return null;

        if (!flexible)
        {
            schedule.IsFlexible = false;
            schedule.FlexibleNormMinutes = null;
            schedule.FlexibleNormPeriod = FlexibleNormPeriod.Day;
            return null;
        }

        FlexibleNormPeriod? period = request.FlexibleNormPeriod?.Trim().ToLowerInvariant() switch
        {
            null or "" or "day" => FlexibleNormPeriod.Day,
            "week" => FlexibleNormPeriod.Week,
            _ => null,
        };
        if (period is null)
            return "FlexibleNormPeriodInvalid";
        if (request.FlexibleNormMinutes is < 0)
            return "FlexibleNormNegative";
        var norm = request.FlexibleNormMinutes is > 0 ? request.FlexibleNormMinutes : null;
        if (norm > (period == FlexibleNormPeriod.Week ? 7 * 24 * 60 : 24 * 60))
            return "FlexibleNormTooLarge";

        schedule.IsFlexible = true;
        schedule.FlexibleNormMinutes = norm;
        schedule.FlexibleNormPeriod = period.Value;
        // No hours on a flexible shift — stored as the whole day so the row reads honestly, and with no rotation, no
        // per-day hours and no second window. The resolver ignores all of them anyway; clearing them means nothing
        // stale comes back if the shift is ever switched to fixed hours again (the form asks for hours then).
        schedule.ShiftStart = new TimeOnly(0, 0);
        schedule.ShiftEnd = new TimeOnly(23, 59);
        schedule.SecondShiftStart = null;
        schedule.SecondShiftEnd = null;
        schedule.DayHours = null;
        schedule.WorkCycleDays = null;
        schedule.WorkCycleOnDays = 1;
        schedule.WorkCycleAnchor = null;
        return null;
    }

    /// <summary>A flexible request needs no hours — the form hides them — so blank start/end is not an error then.</summary>
    public static bool HoursOptional(ScheduleRequest request) => request.IsFlexible == true;

    /// <summary>How the mode reads in the audit log: «Sabit saatlar» / «Elastik (norma 8 saat/gün)».</summary>
    public static string Describe(bool flexible, int? normMinutes, FlexibleNormPeriod period)
    {
        if (!flexible) return "Sabit saatlar";
        if (normMinutes is not int n) return "Elastik (normasız)";
        var hours = (n / 60.0).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        return $"Elastik (norma {hours} saat/{(period == FlexibleNormPeriod.Week ? "həftə" : "gün")})";
    }

    /// <summary>
    /// Writes a <see cref="AuditEventType.ScheduleModeChanged"/> row when the mode or the norm differs between
    /// <paramref name="before"/> (null for a new shift) and <paramref name="after"/>. The revision row keeps how past
    /// days were judged; this keeps WHO changed it and when — a shift edit writes no audit of its own otherwise.
    /// </summary>
    public static void AuditIfChanged(AppDbContext db, Schedule? before, Schedule after, Guid by, string? ip)
    {
        var now = Describe(after.IsFlexible, after.FlexibleNormMinutes, after.FlexibleNormPeriod);
        string? was = before is null ? null : Describe(before.IsFlexible, before.FlexibleNormMinutes, before.FlexibleNormPeriod);
        if (before is null ? !after.IsFlexible : was == now)
            return;
        db.AuditLogs.Add(new AuditLog
        {
            EmployeeId = by,
            EventType = AuditEventType.ScheduleModeChanged,
            Reason = before is null
                ? $"By {by}: növbə «{after.Name}» ({after.Id}) yaradıldı — {now}"
                : $"By {by}: növbə «{after.Name}» ({after.Id}): {was} → {now}",
            IpAddress = ip,
        });
    }
}
