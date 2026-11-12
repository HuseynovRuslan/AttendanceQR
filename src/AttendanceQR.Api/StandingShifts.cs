using System.Text.Json;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api;

/// <summary>
/// Changing somebody's standing shift — one shift, or a rotation with no end — without moving their past.
///
/// The change closes whatever was running the day before <c>from</c> and opens the new one from <c>from</c>. A person
/// who never had an assignment (everybody before 2026-11-12) first gets what they were on written down as running up
/// to that day, so the days before resolve exactly as they did. Every writer of a standing shift comes through here —
/// the employee form (admin and manager), «bulk-schedule», a new person's first shift and the plan screen — so none of
/// them can move history by accident, and every change lands in the plan's history with what it was before.
/// </summary>
public static class StandingShifts
{
    /// <summary>Where an assignment written for «everything before» starts.</summary>
    public static readonly DateOnly Beginning = new(2000, 1, 1);

    private sealed record Row(Guid Id, DateOnly From, DateOnly? Until, string Steps);

    /// <summary>The steps of a single shift: its id, or empty for «own hours».</summary>
    public static string StepsOf(Guid? scheduleId) => scheduleId?.ToString() ?? string.Empty;

    /// <summary>
    /// Puts <paramref name="employee"/> on <paramref name="steps"/> from <paramref name="from"/> on. Returns the history
    /// item (before → after), or null when nothing changes. The caller saves.
    /// </summary>
    public static async Task<ShiftPlanChangeItem?> ChangeAsync(
        AppDbContext db, Employee employee, string steps, DateOnly from,
        IReadOnlyDictionary<Guid, Schedule> schedules, CancellationToken ct)
    {
        var rows = await db.ShiftAssignments.Where(a => a.EmployeeId == employee.Id).OrderBy(a => a.From).ToListAsync(ct);
        var running = rows.LastOrDefault(a => a.Covers(from));
        var before = running?.Steps ?? StepsOf(employee.ScheduleId);
        if (before == steps && (rows.Count == 0 ? true : running is { Until: null } && running.From <= from))
        {
            employee.ScheduleId = FirstShiftOf(steps);
            return null;
        }

        var beforeJson = Json(rows);
        if (rows.Count == 0 && from > Beginning)
        {
            var past = new ShiftAssignment
            {
                EmployeeId = employee.Id, From = Beginning, Until = from.AddDays(-1), Steps = StepsOf(employee.ScheduleId),
            };
            db.ShiftAssignments.Add(past);
            rows.Add(past);
        }

        foreach (var row in rows.ToList())
        {
            if (row.From >= from)
            {
                db.ShiftAssignments.Remove(row);
                rows.Remove(row);
            }
            else if (row.Until is null || row.Until >= from)
                row.Until = from.AddDays(-1);
        }

        var next = new ShiftAssignment { EmployeeId = employee.Id, From = from, Steps = steps };
        db.ShiftAssignments.Add(next);
        rows.Add(next);
        employee.ScheduleId = FirstShiftOf(steps);

        return new ShiftPlanChangeItem
        {
            EmployeeId = employee.Id,
            Date = from,
            Standing = true,
            Before = before,
            After = steps,
            BeforeLabel = Label(before, schedules),
            AfterLabel = Label(steps, schedules),
            BeforeJson = beforeJson,
            AfterJson = Json(rows),
        };
    }

    /// <summary>
    /// Puts the person's assignments back as <paramref name="item"/> found them — only when they are still exactly as
    /// that change left them; anything changed since wins and the undo says so.
    /// </summary>
    public static async Task<bool> UndoAsync(AppDbContext db, Employee employee, ShiftPlanChangeItem item, CancellationToken ct)
    {
        var rows = await db.ShiftAssignments.Where(a => a.EmployeeId == employee.Id).OrderBy(a => a.From).ToListAsync(ct);
        if (Json(rows) != item.AfterJson)
            return false;

        var wanted = JsonSerializer.Deserialize<List<Row>>(item.BeforeJson ?? "[]") ?? [];
        foreach (var row in rows)
        {
            if (wanted.FirstOrDefault(w => w.Id == row.Id) is { } keep)
            {
                row.From = keep.From;
                row.Until = keep.Until;
                row.Steps = keep.Steps;
            }
            else
                db.ShiftAssignments.Remove(row);
        }
        foreach (var missing in wanted.Where(w => rows.All(r => r.Id != w.Id)))
            db.ShiftAssignments.Add(new ShiftAssignment { Id = missing.Id, EmployeeId = employee.Id, From = missing.From, Until = missing.Until, Steps = missing.Steps });

        employee.ScheduleId = FirstShiftOf(item.Before ?? string.Empty) ?? (string.IsNullOrEmpty(item.Before) ? null : employee.ScheduleId);
        return true;
    }

    /// <summary>The company's date today (every company is on Baku time).</summary>
    public static DateOnly Today() => ScheduleRevisions.Today(ScheduleRevisions.Zone(null));

    /// <summary>
    /// The employee form's change of one person's shift: from today, into the plan's history. A person being
    /// created has no past to keep, so their first shift is simply set.
    /// </summary>
    public static async Task SetFromFormAsync(AppDbContext db, Employee employee, Guid? scheduleId, Guid? actorId, CancellationToken ct)
    {
        if (db.Entry(employee).State == EntityState.Added || employee.ScheduleId == scheduleId)
        {
            employee.ScheduleId = scheduleId;
            return;
        }
        var schedules = await db.Schedules.AsNoTracking().ToDictionaryAsync(s => s.Id, ct);
        var item = await ChangeAsync(db, employee, StepsOf(scheduleId), Today(), schedules, ct);
        if (item is null)
            return;
        var change = new ShiftPlanChange
        {
            LocationId = employee.LocationId, CreatedByEmployeeId = actorId, People = 1, Days = 1,
            Summary = $"{employee.FullName}: daimi növbə — {item.BeforeLabel} → {item.AfterLabel}",
        };
        item.ChangeId = change.Id;
        db.ShiftPlanChanges.Add(change);
        db.ShiftPlanChangeItems.Add(item);
    }

    private static Guid? FirstShiftOf(string steps) =>
        new ShiftAssignment { Steps = steps }.FirstShift();

    private static string Json(IEnumerable<ShiftAssignment> rows) =>
        JsonSerializer.Serialize(rows.OrderBy(r => r.From).Select(r => new Row(r.Id, r.From, r.Until, r.Steps)));

    /// <summary>«Səhər 06:00–15:00», «Səhər ×2 · Gecə ×2 · İstirahət ×2», «Öz saatları».</summary>
    public static string Label(string? steps, IReadOnlyDictionary<Guid, Schedule> schedules)
    {
        var list = new ShiftAssignment { Steps = steps ?? string.Empty }.StepList();
        if (list.Length == 0)
            return "Öz saatları (növbəsiz)";
        if (list.Length == 1)
            return DayLabel(list[0], schedules);
        var runs = new List<(string Name, int Count)>();
        foreach (var step in list)
        {
            var name = step == ShiftAssignment.Rest ? "İstirahət" : Guid.TryParse(step, out var id) && schedules.TryGetValue(id, out var s) ? s.Name : "?";
            if (runs.Count > 0 && runs[^1].Name == name)
                runs[^1] = (name, runs[^1].Count + 1);
            else
                runs.Add((name, 1));
        }
        return "Növbəli: " + string.Join(" · ", runs.Select(r => r.Count > 1 ? $"{r.Name} ×{r.Count}" : r.Name));
    }

    /// <summary>One day's label: a shift with its hours, the rest day, or no plan.</summary>
    public static string DayLabel(string? token, IReadOnlyDictionary<Guid, Schedule> schedules)
    {
        if (string.IsNullOrEmpty(token))
            return "Öz növbəsi";
        if (token == ShiftAssignment.Rest)
            return "İstirahət";
        if (Guid.TryParse(token, out var id) && schedules.TryGetValue(id, out var s))
            return s.IsRestDay ? "İstirahət" : $"{s.Name} {s.ShiftStart:HH\\:mm}–{s.ShiftEnd:HH\\:mm}";
        return "Silinmiş növbə";
    }
}
