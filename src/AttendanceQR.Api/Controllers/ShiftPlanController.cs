using AttendanceQR.Api.Contracts;
using AttendanceQR.Application.Common;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Access;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// «Növbə planı» — who works which shift on which day, for a branch, a day to a month at a time.
///
/// A plan is not a new kind of record. One person's one day is a <see cref="ShiftOverride"/> — the row a cover day
/// always was — pointing at a shift, or at the company's «İstirahət». Every resolver already reads those rows
/// (<see cref="ShiftOverrideMap"/>, <see cref="ShiftResolver"/>), so a planned day reaches the board, the tabel, the
/// nightly summary, the scan's night rule and the reminder without a line changed there. A day nobody planned is
/// judged by the person's own shift, as before; a planned day by its plan; a scan on a day off is kept as unplanned
/// work, never thrown away and never an absence.
///
/// Recurrence is expanded into days when it is saved, not kept as a rule: what a day was planned to be is then a
/// fact about that day, with a name and a time against it, and editing one day of a rotation is editing one row.
/// A standing shift with no end is the person's own shift (<c>bulk-schedule</c>), not three hundred rows.
/// </summary>
[Permission(AccessPermissions.SchedulesAssign)]
[ApiController]
[Authorize(Roles = "Admin,Manager")]
[Route("api/admin/shift-plan")]
public class ShiftPlanController : ControllerBase
{
    /// <summary>A month on screen, with the days either side of it a calendar shows.</summary>
    public const int MaxViewDays = 42;
    /// <summary>A quarter at a time: a longer plan is a standing shift, or the next quarter's plan.</summary>
    public const int MaxPlanDays = 92;
    public const int MaxPeople = 500;
    public const int MaxCells = 20_000;
    /// <summary>How far back a standing shift may start: a change made a little late, not a rewrite of the year.</summary>
    public const int MaxStandingBackDays = 31;
    /// <summary>How long a change can still be undone from its history.</summary>
    public const int UndoDays = 30;

    private readonly AppDbContext _db;
    private readonly IDailySummaryService _summaries;
    private readonly TimeZoneInfo _zone;
    private readonly ILogger<ShiftPlanController>? _log;

    public ShiftPlanController(AppDbContext db, IDailySummaryService summaries, AppOptions? appOptions = null, ILogger<ShiftPlanController>? log = null)
    {
        _db = db;
        _summaries = summaries;
        _log = log;
        _zone = ScheduleRevisions.Zone(appOptions?.TimeZone);
    }

    /// <summary>The branches this person may plan: every active one for an admin, their own for a manager.</summary>
    [HttpGet("locations")]
    public async Task<IActionResult> Locations()
    {
        var ct = HttpContext.RequestAborted;
        var scope = await LocationScopeRules.RequestScopeAsync(_db, User.EmployeeId(), User.Role(), AccessPermissions.SchedulesAssign, ct);
        var rows = await _db.Locations.AsNoTracking()
            .Where(l => l.IsActive)
            .Select(l => new { id = l.Id, name = l.Name })
            .ToListAsync(ct);
        return Ok(rows.Where(l => scope.Company || scope.Branches.Contains(l.id)).OrderBy(l => l.name, StringComparer.Create(new System.Globalization.CultureInfo("az"), true)));
    }

    /// <summary>One branch's people and their days, <paramref name="from"/> to <paramref name="to"/>.</summary>
    [HttpGet]
    public async Task<IActionResult> Grid([FromQuery] Guid locationId, [FromQuery] DateOnly from, [FromQuery] DateOnly to)
    {
        var ct = HttpContext.RequestAborted;
        if (to < from || to.DayNumber - from.DayNumber + 1 > MaxViewDays)
            return BadRequest(new { error = "RangeInvalid", maxDays = MaxViewDays });

        var scope = await LocationScopeRules.RequestScopeAsync(_db, User.EmployeeId(), User.Role(), AccessPermissions.SchedulesAssign, ct);
        if (!scope.Company && !scope.Branches.Contains(locationId))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "OutOfScope" });

        var location = await _db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.Id == locationId, ct);
        if (location is null)
            return NotFound(new { error = "LocationNotFound" });

        var people = await _db.Employees.AsNoTracking()
            .Where(e => e.IsActive && e.LocationId == locationId)
            .Select(e => new
            {
                e.Id, e.FullName, e.Position, e.Role, e.ScheduleId,
                e.WorkStart, e.WorkEnd, e.WorkCycleDays, e.WorkCycleOnDays, e.WorkCycleAnchor,
            })
            .ToListAsync(ct);
        var ids = people.Select(p => p.Id).ToList();

        var schedules = await _db.Schedules.AsNoTracking().ToDictionaryAsync(s => s.Id, ct);
        var map = await ShiftOverrideMap.LoadAsync(_db, from, to, ids, ct);
        var holidays = (await _db.NonWorkingDays.AsNoTracking()
                .Where(d => d.Date >= from && d.Date <= to && (d.LocationId == null || d.LocationId == locationId))
                .Select(d => new { d.Date, d.Description })
                .ToListAsync(ct))
            .GroupBy(d => d.Date).ToDictionary(g => g.Key, g => g.First().Description);
        var leaves = await _db.LeaveRecords.AsNoTracking()
            .Where(l => ids.Contains(l.EmployeeId) && l.FromDate <= to && l.ToDate >= from)
            .Select(l => new { l.EmployeeId, l.FromDate, l.ToDate, l.Type })
            .ToListAsync(ct);
        var records = await _db.AttendanceRecords.AsNoTracking()
            .Where(r => ids.Contains(r.EmployeeId) && r.AttendanceDate >= from && r.AttendanceDate <= to)
            .Select(r => new { r.EmployeeId, r.AttendanceDate, r.CheckInAtUtc, r.CheckOutAtUtc })
            .ToListAsync(ct);
        var worked = records
            .GroupBy(r => (r.EmployeeId, r.AttendanceDate))
            .ToDictionary(g => g.Key, g => (
                In: g.Where(r => r.CheckInAtUtc != null).Select(r => r.CheckInAtUtc!.Value).DefaultIfEmpty().Min(),
                Out: g.Any(r => r.CheckInAtUtc != null && r.CheckOutAtUtc == null)
                    ? (DateTime?)null
                    : g.Where(r => r.CheckOutAtUtc != null).Select(r => (DateTime?)r.CheckOutAtUtc!.Value).DefaultIfEmpty().Max()));

        var days = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(i => from.AddDays(i)).ToList();
        string? Local(DateTime? utc) => utc is DateTime u && u != default ? TimeZoneInfo.ConvertTimeFromUtc(u, _zone).ToString("HH:mm") : null;

        var rows = people.Select(p => new
        {
            id = p.Id,
            name = p.FullName,
            position = p.Position,
            role = p.Role.ToString(),
            scheduleId = p.ScheduleId,
            days = days.Select(date =>
            {
                var planned = map.On(p.Id, date);
                var schedule = map.ScheduleFor(p.Id, date, p.ScheduleId, schedules);
                var shift = EffectiveShift.Resolve(p.WorkStart, p.WorkEnd, p.WorkCycleDays, p.WorkCycleOnDays, p.WorkCycleAnchor, schedule, location);
                var hours = shift.HoursOn(date);
                var holiday = holidays.GetValueOrDefault(date);
                var rest = planned is Guid pid && schedules.TryGetValue(pid, out var plannedSchedule) && plannedSchedule.IsRestDay;
                var working = shift.IsWorkingDay(date) && holiday is null;
                var leave = leaves.FirstOrDefault(l => l.EmployeeId == p.Id && l.FromDate <= date && l.ToDate >= date);
                worked.TryGetValue((p.Id, date), out var times);
                return new
                {
                    date,
                    kind = planned is null ? (working ? "base" : "off") : rest ? "rest" : "plan",
                    scheduleId = rest ? null : planned ?? map.EffectiveScheduleId(p.Id, date, p.ScheduleId),
                    shift = rest ? "İstirahət" : schedule?.Name,
                    start = rest ? null : hours.Start.ToString("HH:mm"),
                    end = rest ? null : hours.End.ToString("HH:mm"),
                    overnight = !rest && shift.IsOvernightOn(date),
                    working,
                    holiday,
                    leave = leave?.Type.ToString(),
                    checkIn = Local(times.In),
                    checkOut = Local(times.Out),
                };
            }),
        });

        return Ok(new
        {
            locationId,
            from,
            to,
            today = ScheduleRevisions.Today(_zone),
            schedules = schedules.Values
                .Where(s => !s.IsRestDay && s.RevisionOfId == null && (s.LocationId == null || s.LocationId == locationId))
                .OrderBy(s => s.ShiftStart).ThenBy(s => s.Name)
                .Select(s => new
                {
                    id = s.Id, name = s.Name, start = s.ShiftStart.ToString("HH:mm"), end = s.ShiftEnd.ToString("HH:mm"),
                    overnight = s.ShiftEnd < s.ShiftStart, locationId = s.LocationId,
                }),
            employees = rows,
        });
    }

    /// <summary>
    /// Writes a plan — or, with <c>Preview</c>, says what it would change and writes nothing. Days: the dates or range
    /// in the request, one shift or a rotation. Standing: from a date with no end, as the person's standing shift.
    /// </summary>
    [HttpPost("apply")]
    public async Task<IActionResult> Apply([FromBody] ShiftPlanApplyRequest request)
    {
        var ct = HttpContext.RequestAborted;
        var me = User.EmployeeId();
        var today = ScheduleRevisions.Today(_zone);

        var employeeIds = (request.EmployeeIds ?? []).Distinct().ToList();
        if (employeeIds.Count == 0)
            return BadRequest(new { error = "NoEmployees" });
        if (employeeIds.Count > MaxPeople)
            return BadRequest(new { error = "TooManyEmployees", max = MaxPeople });
        if (request.Steps is not { Count: > 0 and <= 28 })
            return BadRequest(new { error = "StepsInvalid" });

        // Each step: a shift's id, «rest» or «clear».
        var tokens = new List<string?>();
        var shiftIds = new HashSet<Guid>();
        foreach (var step in request.Steps)
        {
            switch (step?.Trim().ToLowerInvariant())
            {
                case "rest": tokens.Add(ShiftAssignment.Rest); break;
                case "clear": tokens.Add(null); break;
                default:
                    if (!Guid.TryParse(step, out var id))
                        return BadRequest(new { error = "StepsInvalid" });
                    shiftIds.Add(id);
                    tokens.Add(id.ToString());
                    break;
            }
        }

        var shifts = await _db.Schedules.Templates().Where(s => shiftIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        if (shifts.Count != shiftIds.Count)
            return BadRequest(new { error = "ScheduleNotFound" });

        var people = await _db.Employees.Where(e => employeeIds.Contains(e.Id) && e.IsActive).ToListAsync(ct);
        if (people.Count != employeeIds.Count)
            return BadRequest(new { error = "EmployeeNotFound" });

        var outOfScope = 0;
        foreach (var person in people)
            if (!await LocationScopeRules.CanScheduleEmployeeAsync(_db, me, User.Role(), person.Id, ct))
                outOfScope++;
        if (outOfScope > 0)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "OutOfScope", count = outOfScope });

        // A shift that belongs to one branch is that branch's: the same rule as putting somebody on it for good.
        var misfits = people
            .Where(p => shifts.Values.Any(s => ScheduleAssignmentRule.Refusal(s.LocationId, p.LocationId) is not null))
            .Select(p => p.FullName)
            .ToList();
        if (misfits.Count > 0)
            return BadRequest(new { error = "ScheduleBelongsToOtherBranch", employees = misfits.Take(10) });

        var catalogue = await _db.Schedules.AsNoTracking().ToDictionaryAsync(s => s.Id, ct);
        var locationId = request.LocationId
            ?? (people.Select(p => p.LocationId).Distinct().Count() == 1 ? people[0].LocationId : null);

        return request.Standing
            ? await StandingAsync(request, people, tokens, catalogue, locationId, today, me, ct)
            : await DaysAsync(request, people, tokens, shifts, catalogue, locationId, today, me, ct);
    }

    private async Task<IActionResult> StandingAsync(
        ShiftPlanApplyRequest request, List<Employee> people, List<string?> tokens,
        Dictionary<Guid, Schedule> catalogue, Guid? locationId, DateOnly today, Guid me, CancellationToken ct)
    {
        if (request.From is not DateOnly from)
            return BadRequest(new { error = "NoDays" });
        if (tokens.Any(t => t is null))
            return BadRequest(new { error = "StepsInvalid" });
        // A standing shift may start a little in the past (a change made late); not a year back.
        if (from < today.AddDays(-MaxStandingBackDays))
            return BadRequest(new { error = "StandingTooFarBack", maxDays = MaxStandingBackDays });

        var steps = string.Join(',', tokens);
        var label = StandingShifts.Label(steps, catalogue);
        var map = await ShiftOverrideMap.LoadAsync(_db, from, from, people.Select(p => p.Id).ToList(), ct);
        var transitions = people
            .Select(p => (Before: StandingShifts.Label(map.StandingOn(p.Id, from)?.Steps ?? StandingShifts.StepsOf(p.ScheduleId), catalogue), After: label))
            .Where(t => t.Before != t.After)
            .GroupBy(t => t)
            .Select(g => new { from = g.Key.Before, to = g.Key.After, count = g.Count() })
            .OrderByDescending(t => t.count)
            .ToList();
        var pastDays = Math.Max(0, today.DayNumber - from.DayNumber);

        if (request.Preview)
            return Ok(new
            {
                preview = true, standing = true, people = people.Count, from, label,
                changedPeople = transitions.Sum(t => t.count), pastDays, changes = transitions,
            });
        if (pastDays > 0 && !request.ConfirmPast)
            return Conflict(new { error = "PastDatesNeedConfirm", pastDays, people = people.Count });

        var change = new ShiftPlanChange
        {
            LocationId = locationId, CreatedByEmployeeId = me, People = 0, Days = 1,
            Summary = $"{people.Count} işçi, {from:dd.MM.yyyy}-dən müddətsiz: {label}",
        };
        foreach (var person in people)
        {
            if (await StandingShifts.ChangeAsync(_db, person, steps, from, catalogue, ct) is not { } item)
                continue;
            item.ChangeId = change.Id;
            _db.ShiftPlanChangeItems.Add(item);
            change.People++;
        }
        if (change.People == 0)
            return Ok(new { ok = true, set = 0, cleared = 0, unchanged = people.Count, people = 0, rebuiltDays = 0, rebuildPending = 0, changeId = (Guid?)null });

        _db.ShiftPlanChanges.Add(change);
        var worked = Enumerable.Range(0, pastDays).Select(i => from.AddDays(i)).ToList();
        await PlanRebuilds.MarkAsync(_db, worked, ct);
        await _db.SaveChangesAsync(ct);
        var (rebuilt, pending) = await PlanRebuilds.RunAsync(_db, _summaries, worked, _log, ct);

        return Ok(new { ok = true, set = change.People, cleared = 0, unchanged = people.Count - change.People, people = change.People, rebuiltDays = rebuilt, rebuildPending = pending, changeId = change.Id });
    }

    private async Task<IActionResult> DaysAsync(
        ShiftPlanApplyRequest request, List<Employee> people, List<string?> tokens, Dictionary<Guid, Schedule> shifts,
        Dictionary<Guid, Schedule> catalogue, Guid? locationId, DateOnly today, Guid me, CancellationToken ct)
    {
        // The days, oldest first, each with its place in a rotation (counted from the first day).
        List<DateOnly> dates;
        DateOnly anchor;
        if (request.Dates is { Count: > 0 } explicitDates)
        {
            dates = explicitDates.Distinct().Order().ToList();
            anchor = dates[0];
            if (dates.Count > MaxPlanDays * 4)
                return BadRequest(new { error = "TooManyDays", max = MaxPlanDays * 4 });
        }
        else if (request.From is DateOnly from && request.To is DateOnly to && to >= from)
        {
            if (to.DayNumber - from.DayNumber + 1 > MaxPlanDays)
                return BadRequest(new { error = "RangeTooLong", maxDays = MaxPlanDays });
            dates = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(i => from.AddDays(i)).ToList();
            anchor = from;
        }
        else
            return BadRequest(new { error = "NoDays" });

        if (dates[^1] > today.AddYears(1))
            return BadRequest(new { error = "DateTooFar" });
        if ((long)dates.Count * people.Count > MaxCells)
            return BadRequest(new { error = "TooManyCells", max = MaxCells });

        // What each day becomes: a shift id, «rest», null to clear — or skipped (outside the weekdays, not rest).
        var plan = new List<(DateOnly Date, string? Target)>();
        foreach (var date in dates)
        {
            if (request.Weekdays is int mask && (mask & (1 << (int)date.DayOfWeek)) == 0)
            {
                if (request.RestOnOtherDays)
                    plan.Add((date, ShiftAssignment.Rest));
                continue;
            }
            plan.Add((date, tokens[(date.DayNumber - anchor.DayNumber) % tokens.Count]));
        }

        var first = plan.Count > 0 ? plan[0].Date : dates[0];
        var last = plan.Count > 0 ? plan[^1].Date : dates[^1];
        var ids = people.Select(p => p.Id).ToList();
        var existing = await _db.ShiftOverrides
            .Where(o => ids.Contains(o.EmployeeId) && o.Date >= first && o.Date <= last)
            .ToDictionaryAsync(o => (o.EmployeeId, o.Date), ct);
        string? TokenOf(ShiftOverride? row) => row is null ? null
            : catalogue.TryGetValue(row.ScheduleId, out var s) && s.IsRestDay ? ShiftAssignment.Rest : row.ScheduleId.ToString();

        // A day without a plan runs on the person's own shift — and «öz növbəsi» alone tells the reader nothing, so the
        // labels name it: the standing shift that covered that date, its hours, or that it was not a working day.
        var standingOnly = new ShiftOverrideMap(new Dictionary<(Guid, DateOnly), Guid>(), await _db.ShiftAssignments.AsNoTracking()
            .Where(a => ids.Contains(a.EmployeeId) && a.From <= last && (a.Until == null || a.Until >= first))
            .ToListAsync(ct));
        var placeIds = people.Select(p => p.LocationId).Distinct().ToList();
        var places = await _db.Locations.AsNoTracking().Where(l => placeIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
        string LabelOf(Employee person, DateOnly date, string? token)
        {
            if (token is not null || !places.TryGetValue(person.LocationId, out var place))
                return StandingShifts.DayLabel(token, catalogue);
            var own = standingOnly.ScheduleFor(person.Id, date, person.ScheduleId, catalogue);
            var shift = EffectiveShift.Resolve(person, own, place);
            if (!shift.IsWorkingDay(date))
                return "Öz növbəsi: İstirahət günü";
            var hours = shift.HoursOn(date);
            return own is { IsRestDay: false } named
                ? $"Öz növbəsi: {named.Name} {hours.Start:HH\\:mm}–{hours.End:HH\\:mm}"
                : $"Öz növbəsi: {hours.Start:HH\\:mm}–{hours.End:HH\\:mm}";
        }

        var cells = new List<(Employee Person, DateOnly Date, string? Before, string? After)>();
        var unchanged = 0;
        foreach (var person in people)
            foreach (var (date, target) in plan)
            {
                var before = TokenOf(existing.GetValueOrDefault((person.Id, date)));
                if (before == target) { unchanged++; continue; }
                cells.Add((person, date, before, target));
            }

        var pastDays = cells.Where(c => c.Date < today).Select(c => c.Date).Distinct().Count();
        var transitions = cells
            .GroupBy(c => (From: LabelOf(c.Person, c.Date, c.Before), To: LabelOf(c.Person, c.Date, c.After)))
            .Select(g => new { from = g.Key.From, to = g.Key.To, count = g.Count() })
            .OrderByDescending(t => t.count)
            .ToList();

        if (request.Preview)
            return Ok(new
            {
                preview = true, standing = false, people = people.Count, days = plan.Select(p => p.Date).Distinct().Count(),
                newDays = cells.Count(c => c.Before is null && c.After is not null),
                replacedDays = cells.Count(c => c.Before is not null && c.After is not null),
                clearedDays = cells.Count(c => c.After is null),
                unchangedDays = unchanged, pastDays, changes = transitions.Take(12),
            });
        if (pastDays > 0 && !request.ConfirmPast)
            return Conflict(new { error = "PastDatesNeedConfirm", pastDays, people = people.Count });

        Schedule? restDay = null;
        if (cells.Any(c => c.After == ShiftAssignment.Rest))
        {
            restDay = await _db.Schedules.FirstOrDefaultAsync(s => s.IsRestDay && s.RevisionOfId == null, ct);
            if (restDay is null)
            {
                restDay = new Schedule
                {
                    Name = "İstirahət", IsRestDay = true, WorkDaysMask = 0, LateThresholdMinutes = 15,
                    ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
                };
                _db.Schedules.Add(restDay);
            }
        }

        var note = request.Note?.Trim();
        note = string.IsNullOrEmpty(note) ? "Növbə planı" : note[..Math.Min(note.Length, 200)];
        var now = DateTime.UtcNow;
        var what = string.Join(", ", tokens.Select(t => t is null ? "öz növbəsi" : StandingShifts.DayLabel(t, catalogue).Split(' ')[0]).Distinct());
        var touchedFirst = cells.Count > 0 ? cells.Min(c => c.Date) : first;
        var touchedLast = cells.Count > 0 ? cells.Max(c => c.Date) : last;
        var change = new ShiftPlanChange
        {
            LocationId = locationId, CreatedByEmployeeId = me,
            People = cells.Select(c => c.Person.Id).Distinct().Count(), Days = cells.Select(c => c.Date).Distinct().Count(),
            Summary = $"{cells.Select(c => c.Person.Id).Distinct().Count()} işçi, {Span(touchedFirst, touchedLast)}: {what}",
        };

        int set = 0, cleared = 0;
        foreach (var (person, date, before, after) in cells)
        {
            var row = existing.GetValueOrDefault((person.Id, date));
            if (after is null)
            {
                _db.ShiftOverrides.Remove(row!);
                cleared++;
            }
            else
            {
                var target = after == ShiftAssignment.Rest ? restDay!.Id : Guid.Parse(after);
                if (row is null)
                    _db.ShiftOverrides.Add(new ShiftOverride
                    {
                        EmployeeId = person.Id, Date = date, ScheduleId = target, Note = note, CreatedByEmployeeId = me, CreatedAtUtc = now,
                    });
                else
                {
                    row.ScheduleId = target;
                    row.Note = note;
                    row.CreatedByEmployeeId = me;
                    row.CreatedAtUtc = now;
                }
                set++;
            }
            _db.ShiftPlanChangeItems.Add(new ShiftPlanChangeItem
            {
                ChangeId = change.Id, EmployeeId = person.Id, Date = date, Before = before, After = after,
                BeforeLabel = LabelOf(person, date, before), AfterLabel = LabelOf(person, date, after),
            });
        }

        if (cells.Count == 0)
            return Ok(new { ok = true, set, cleared, unchanged, people = 0, rebuiltDays = 0, rebuildPending = 0, changeId = (Guid?)null });

        _db.ShiftPlanChanges.Add(change);
        foreach (var personId in cells.Select(c => c.Person.Id).Distinct())
            _db.AuditLogs.Add(new AuditLog
            {
                EmployeeId = personId, EventType = AuditEventType.ShiftPlanChanged,
                Reason = $"{change.Summary} · dəyişiklik {change.Id} · {me}",
            });

        var worked = cells.Where(c => c.Date < today).Select(c => c.Date).Distinct().ToList();
        await PlanRebuilds.MarkAsync(_db, worked, ct);
        await _db.SaveChangesAsync(ct);   // the plan, its history and the stale-day notes: one save
        var (rebuilt, pending) = await PlanRebuilds.RunAsync(_db, _summaries, worked, _log, ct);

        return Ok(new { ok = true, set, cleared, unchanged, people = change.People, rebuiltDays = rebuilt, rebuildPending = pending, changeId = change.Id });
    }

    /// <summary>The days a change touched, as the history heads it: «14.11.2026» or «12.11–13.11.2026» — not the range asked for.</summary>
    private static string Span(DateOnly first, DateOnly last)
        => first == last ? $"{first:dd.MM.yyyy}" : $"{first:dd.MM}–{last:dd.MM.yyyy}";

    /// <summary>A branch's recent changes, newest first: who, when, and every day's shift before and after.</summary>
    [HttpGet("changes")]
    public async Task<IActionResult> Changes([FromQuery] Guid locationId, [FromQuery] int take = 30)
    {
        var ct = HttpContext.RequestAborted;
        var scope = await LocationScopeRules.RequestScopeAsync(_db, User.EmployeeId(), User.Role(), AccessPermissions.SchedulesAssign, ct);
        if (!scope.Company && !scope.Branches.Contains(locationId))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "OutOfScope" });

        var changes = await _db.ShiftPlanChanges.AsNoTracking()
            .Where(c => c.LocationId == locationId)
            .OrderByDescending(c => c.CreatedAtUtc)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(ct);
        var changeIds = changes.Select(c => c.Id).ToList();
        var items = await _db.ShiftPlanChangeItems.AsNoTracking().Where(i => changeIds.Contains(i.ChangeId)).ToListAsync(ct);
        var nameIds = items.Select(i => i.EmployeeId)
            .Concat(changes.Where(c => c.CreatedByEmployeeId != null).Select(c => c.CreatedByEmployeeId!.Value))
            .Concat(changes.Where(c => c.UndoneByEmployeeId != null).Select(c => c.UndoneByEmployeeId!.Value))
            .Distinct().ToList();
        var names = await _db.Employees.AsNoTracking().Where(e => nameIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        var undoWindow = DateTime.UtcNow.AddDays(-UndoDays);
        string Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, _zone).ToString("yyyy-MM-dd HH:mm");

        return Ok(changes.Select(c =>
        {
            var mine = items.Where(i => i.ChangeId == c.Id)
                .OrderBy(i => names.GetValueOrDefault(i.EmployeeId)).ThenBy(i => i.Date).ToList();
            return new
            {
                id = c.Id,
                at = Local(c.CreatedAtUtc),
                by = c.CreatedByEmployeeId is Guid b ? names.GetValueOrDefault(b) : null,
                summary = c.Summary,
                people = c.People,
                days = c.Days,
                isUndo = c.UndoOfId != null,
                undoneAt = c.UndoneAtUtc is DateTime u ? Local(u) : null,
                undoneBy = c.UndoneByEmployeeId is Guid ub ? names.GetValueOrDefault(ub) : null,
                undoable = c.UndoneAtUtc == null && c.UndoOfId == null && c.CreatedAtUtc > undoWindow,
                items = mine.Take(200).Select(i => new
                {
                    employee = names.GetValueOrDefault(i.EmployeeId) ?? "—",
                    date = i.Date,
                    standing = i.Standing,
                    before = i.BeforeLabel,
                    after = i.AfterLabel,
                }),
                more = Math.Max(0, mine.Count - 200),
            };
        }));
    }

    /// <summary>
    /// Puts back what a change replaced. A day somebody changed again since is left as it is now — the later change
    /// wins — and the answer says how many. The undo is a change of its own, so it is in the history too.
    /// </summary>
    [HttpPost("changes/{id:guid}/undo")]
    public async Task<IActionResult> Undo(Guid id, [FromBody] ShiftPlanUndoRequest? request)
    {
        var ct = HttpContext.RequestAborted;
        var me = User.EmployeeId();
        var today = ScheduleRevisions.Today(_zone);

        var change = await _db.ShiftPlanChanges.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (change is null)
            return NotFound(new { error = "NotFound" });
        if (change.UndoneAtUtc is not null)
            return Conflict(new { error = "AlreadyUndone" });
        if (change.UndoOfId is not null)
            return BadRequest(new { error = "UndoOfUndo" });
        if (change.CreatedAtUtc < DateTime.UtcNow.AddDays(-UndoDays))
            return BadRequest(new { error = "TooOld", days = UndoDays });

        var items = await _db.ShiftPlanChangeItems.Where(i => i.ChangeId == id).ToListAsync(ct);
        var people = await _db.Employees.Where(e => items.Select(i => i.EmployeeId).Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        foreach (var person in people.Values)
            if (!await LocationScopeRules.CanScheduleEmployeeAsync(_db, me, User.Role(), person.Id, ct))
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "OutOfScope" });

        var past = items.Where(i => i.Date < today).ToList();
        if (past.Count > 0 && request?.ConfirmPast != true)
            return Conflict(new { error = "PastDatesNeedConfirm", pastDays = Math.Max(1, past.Select(i => i.Date).Distinct().Count()) });

        var catalogue = await _db.Schedules.AsNoTracking().ToDictionaryAsync(s => s.Id, ct);
        var restDay = await _db.Schedules.FirstOrDefaultAsync(s => s.IsRestDay && s.RevisionOfId == null, ct);
        var dayItems = items.Where(i => !i.Standing).ToList();
        var dates = dayItems.Select(i => i.Date).Distinct().ToList();
        var rows = await _db.ShiftOverrides
            .Where(o => people.Keys.Contains(o.EmployeeId) && dates.Contains(o.Date))
            .ToDictionaryAsync(o => (o.EmployeeId, o.Date), ct);

        var undo = new ShiftPlanChange
        {
            LocationId = change.LocationId, CreatedByEmployeeId = me, UndoOfId = change.Id,
            Summary = $"Geri alındı: {change.Summary}",
        };
        int reverted = 0, kept = 0;
        var worked = new List<DateOnly>();

        foreach (var item in dayItems)
        {
            var row = rows.GetValueOrDefault((item.EmployeeId, item.Date));
            var now = row is null ? null
                : catalogue.TryGetValue(row.ScheduleId, out var s) && s.IsRestDay ? ShiftAssignment.Rest : row.ScheduleId.ToString();
            if (now != item.After) { kept++; continue; }

            if (item.Before is null)
                _db.ShiftOverrides.Remove(row!);
            else
            {
                var target = item.Before == ShiftAssignment.Rest
                    ? (restDay ??= _db.Schedules.Add(new Schedule
                    {
                        Name = "İstirahət", IsRestDay = true, WorkDaysMask = 0, LateThresholdMinutes = 15,
                        ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
                    }).Entity).Id
                    : Guid.Parse(item.Before);
                if (row is null)
                    _db.ShiftOverrides.Add(new ShiftOverride { EmployeeId = item.EmployeeId, Date = item.Date, ScheduleId = target, Note = "Geri alındı", CreatedByEmployeeId = me });
                else
                    row.ScheduleId = target;
            }
            AddReverse(undo, item);
            reverted++;
            if (item.Date < today) worked.Add(item.Date);
        }

        foreach (var item in items.Where(i => i.Standing))
        {
            if (!people.TryGetValue(item.EmployeeId, out var person) || !await StandingShifts.UndoAsync(_db, person, item, ct))
            {
                kept++;
                continue;
            }
            AddReverse(undo, item);
            reverted++;
            for (var d = item.Date; d < today; d = d.AddDays(1)) worked.Add(d);
        }

        if (reverted == 0)
            return Conflict(new { error = "NothingToUndo", kept });

        undo.People = items.Select(i => i.EmployeeId).Distinct().Count();
        undo.Days = dates.Count;
        change.UndoneAtUtc = DateTime.UtcNow;
        change.UndoneByEmployeeId = me;
        _db.ShiftPlanChanges.Add(undo);
        await PlanRebuilds.MarkAsync(_db, worked, ct);
        await _db.SaveChangesAsync(ct);
        var (rebuilt, pending) = await PlanRebuilds.RunAsync(_db, _summaries, worked, _log, ct);

        return Ok(new { ok = true, reverted, kept, rebuiltDays = rebuilt, rebuildPending = pending, changeId = undo.Id });

        void AddReverse(ShiftPlanChange to, ShiftPlanChangeItem item) => _db.ShiftPlanChangeItems.Add(new ShiftPlanChangeItem
        {
            ChangeId = to.Id, EmployeeId = item.EmployeeId, Date = item.Date, Standing = item.Standing,
            Before = item.After, After = item.Before, BeforeLabel = item.AfterLabel, AfterLabel = item.BeforeLabel,
            BeforeJson = item.AfterJson, AfterJson = item.BeforeJson,
        });
    }
}
