using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api;

/// <summary>
/// A shift's history, kept when it is edited. Editing a shift's hours used to re-judge every past day on it the
/// next time that day was recomputed — a correction, a leave entry — so a change made for next month quietly
/// rewrote last month's tabel and pay. Now the shift as it stood is kept as a revision valid until yesterday, and
/// <see cref="ShiftResolver"/> hands that revision to any day before the edit. The same rule for the admin's and
/// the manager's edit, so it lives here once.
/// </summary>
public static class ScheduleRevisions
{
    /// <summary>Only real templates: never the company's «İstirahət», never a revision.</summary>
    public static IQueryable<Schedule> Templates(this IQueryable<Schedule> schedules)
        => schedules.Where(s => !s.IsRestDay && s.RevisionOfId == null);

    /// <summary>
    /// Keeps <paramref name="before"/> (a <see cref="ShiftResolver.Snapshot"/> taken before the edit) as the shift
    /// up to yesterday, when the edit changes how a day is judged. Nothing to keep for a shift made today, or when
    /// today's earlier edit already kept yesterday's version.
    /// </summary>
    public static async Task KeepAsync(
        AppDbContext db, Schedule edited, Schedule before, DateOnly today, TimeZoneInfo zone, CancellationToken ct)
    {
        if (!ShiftResolver.JudgesDaysDifferently(before, edited))
            return;
        if (DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(edited.CreatedAtUtc, zone)) >= today)
            return;
        var yesterday = today.AddDays(-1);
        if (await db.Schedules.AnyAsync(s => s.RevisionOfId == edited.Id && s.ValidUntil == yesterday, ct))
            return;

        before.RevisionOfId = edited.Id;
        before.ValidUntil = yesterday;
        before.TenantId = edited.TenantId;
        db.Schedules.Add(before);
    }

    /// <summary>The company's date today.</summary>
    public static DateOnly Today(TimeZoneInfo zone) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));

    public static TimeZoneInfo Zone(string? timeZone)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(timeZone) ? "Asia/Baku" : timeZone);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
