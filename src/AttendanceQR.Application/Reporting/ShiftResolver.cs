using System.Runtime.CompilerServices;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Application.Reporting;

/// <summary>
/// Which schedule answers for one person on one date, once a plan, a rest day and a shift's own history are taken
/// into account. Every resolver reaches it — through <see cref="ShiftOverrideMap.ScheduleFor"/> or, where one
/// shift is looked up on its own, directly — so the board, the tabel, the nightly summary, the scan and the
/// reminder can never disagree about a day.
///
/// It returns a <see cref="Schedule"/> and nothing else, which is what keeps it small: the day's arithmetic
/// (overnight, working-day mask, late threshold, per-day hours) stays where it is and is simply handed the right
/// shift.
/// <list type="bullet">
/// <item>A rest day («İstirahət», <see cref="Schedule.IsRestDay"/>) becomes the person's own shift — the same
/// hours, so a scan that day is still read sensibly — on a day that is not a working day: no «Qayıb», no reminder.
/// A scan on it is kept; it is unplanned work, not an absence.</item>
/// <item>A shift edited after a day was worked answers for that day as it was then: the revision whose
/// <see cref="Schedule.ValidUntil"/> is the first on or after the date (<see cref="Schedule.RevisionOfId"/>).</item>
/// </list>
/// </summary>
public static class ShiftResolver
{
    // Revisions per shift, oldest first, built once per catalogue the callers load (a tabel walks thousands of
    // person-days against the same dictionary).
    private static readonly ConditionalWeakTable<IReadOnlyDictionary<Guid, Schedule>, Dictionary<Guid, Schedule[]>> Index = new();

    /// <summary>
    /// The schedule for <paramref name="date"/>: <paramref name="scheduleId"/> (a plan's, or the person's own) as
    /// it was that day, or — for a rest day — the person's own hours on a day off. Null when there is none, which
    /// leaves the caller on the person's or the branch's hours as before.
    /// </summary>
    public static Schedule? ScheduleOn(
        Guid? scheduleId, Guid? ownScheduleId, DateOnly date, IReadOnlyDictionary<Guid, Schedule> schedules)
    {
        if (scheduleId is not Guid id || !schedules.TryGetValue(id, out var schedule))
            return null;
        if (!schedule.IsRestDay)
            return VersionOn(schedule, date, schedules);

        var own = ownScheduleId is Guid ownId && ownId != id && schedules.TryGetValue(ownId, out var mine) && !mine.IsRestDay
            ? VersionOn(mine, date, schedules)
            : null;
        return RestDayOf(own ?? schedule);
    }

    /// <summary>
    /// The same answer for one person and one date, read from the database: the day's plan (a planned shift, a
    /// cover, a rest day) or their own shift, as it stood that day. For the paths that resolve one day on its own —
    /// the scan, a correction, the app's «today» card — so they agree with the reports to the minute.
    /// </summary>
    public static async Task<Schedule?> ScheduleOnAsync(
        AppDbContext db, Guid employeeId, Guid? ownScheduleId, DateOnly date, CancellationToken ct)
    {
        var map = await ShiftOverrideMap.LoadAsync(db, date, date, [employeeId], ct);
        var schedules = await db.Schedules.AsNoTracking().ToDictionaryAsync(s => s.Id, ct);
        return map.ScheduleFor(employeeId, date, ownScheduleId, schedules);
    }

    /// <summary>
    /// A day off for somebody whose hours are <paramref name="hoursScheduleId"/>'s (as that shift stood on the date):
    /// the same hours, not a working day. With no such shift, the company's «İstirahət» row, or a plain day off.
    /// </summary>
    public static Schedule RestOn(Guid? hoursScheduleId, DateOnly date, IReadOnlyDictionary<Guid, Schedule> schedules)
    {
        if (hoursScheduleId is Guid id && schedules.TryGetValue(id, out var hours) && !hours.IsRestDay)
            return RestDayOf(VersionOn(hours, date, schedules));
        var rest = schedules.Values.FirstOrDefault(s => s.IsRestDay && s.RevisionOfId is null);
        return RestDayOf(rest ?? new Schedule
        {
            Id = Guid.Empty, Name = "İstirahət", ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
            LateThresholdMinutes = 15, WorkDaysMask = 0, IsRestDay = true,
        });
    }

    /// <summary>A shift as it stood on <paramref name="date"/>: the revision that covered it, or the shift itself.</summary>
    public static Schedule VersionOn(Schedule schedule, DateOnly date, IReadOnlyDictionary<Guid, Schedule> schedules)
    {
        if (schedule.RevisionOfId is not null)
            return schedule;
        var revisions = Index.GetValue(schedules, Build).GetValueOrDefault(schedule.Id);
        if (revisions is null)
            return schedule;
        foreach (var revision in revisions)
            if (date <= revision.ValidUntil)
                return revision;
        return schedule;
    }

    private static Dictionary<Guid, Schedule[]> Build(IReadOnlyDictionary<Guid, Schedule> schedules) =>
        schedules.Values
            .Where(s => s.RevisionOfId is not null && s.ValidUntil is not null)
            .GroupBy(s => s.RevisionOfId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.ValidUntil).ToArray());

    /// <summary>The same hours on a day that is not a working day — a copy, never tracked or saved.</summary>
    private static Schedule RestDayOf(Schedule hours) => new()
    {
        Id = hours.Id,
        TenantId = hours.TenantId,
        Name = "İstirahət",
        LocationId = hours.LocationId,
        ShiftStart = hours.ShiftStart,
        ShiftEnd = hours.ShiftEnd,
        DayHours = hours.DayHours,
        SecondShiftStart = hours.SecondShiftStart,
        SecondShiftEnd = hours.SecondShiftEnd,
        LateThresholdMinutes = hours.LateThresholdMinutes,
        WorkDaysMask = 0,
        WorkCycleDays = null,
        WorkCycleAnchor = null,
        IsRestDay = true,
        CreatedAtUtc = hours.CreatedAtUtc,
    };

    /// <summary>The fields that decide how a day is judged: an edit to any of them is kept as a revision.</summary>
    public static bool JudgesDaysDifferently(Schedule before, Schedule after) =>
        before.ShiftStart != after.ShiftStart || before.ShiftEnd != after.ShiftEnd
        || before.SecondShiftStart != after.SecondShiftStart || before.SecondShiftEnd != after.SecondShiftEnd
        || before.LateThresholdMinutes != after.LateThresholdMinutes || before.WorkDaysMask != after.WorkDaysMask
        || before.WorkCycleDays != after.WorkCycleDays || before.WorkCycleOnDays != after.WorkCycleOnDays
        || before.WorkCycleAnchor != after.WorkCycleAnchor || before.DayHours != after.DayHours;

    /// <summary>A detached copy of the fields that judge a day, taken before an edit.</summary>
    public static Schedule Snapshot(Schedule schedule) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = schedule.TenantId,
        Name = schedule.Name,
        LocationId = schedule.LocationId,
        ShiftStart = schedule.ShiftStart,
        ShiftEnd = schedule.ShiftEnd,
        DayHours = schedule.DayHours,
        SecondShiftStart = schedule.SecondShiftStart,
        SecondShiftEnd = schedule.SecondShiftEnd,
        LateThresholdMinutes = schedule.LateThresholdMinutes,
        WorkDaysMask = schedule.WorkDaysMask,
        WorkCycleDays = schedule.WorkCycleDays,
        WorkCycleOnDays = schedule.WorkCycleOnDays,
        WorkCycleAnchor = schedule.WorkCycleAnchor,
        CreatedAtUtc = schedule.CreatedAtUtc,
    };
}
