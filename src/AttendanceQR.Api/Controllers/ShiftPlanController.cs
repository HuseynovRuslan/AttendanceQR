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

    private readonly AppDbContext _db;
    private readonly IDailySummaryService _summaries;
    private readonly TimeZoneInfo _zone;

    public ShiftPlanController(AppDbContext db, IDailySummaryService summaries, AppOptions? appOptions = null)
    {
        _db = db;
        _summaries = summaries;
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
        var overrides = await _db.ShiftOverrides.AsNoTracking()
            .Where(o => ids.Contains(o.EmployeeId) && o.Date >= from && o.Date <= to)
            .ToListAsync(ct);
        var map = new ShiftOverrideMap(overrides.ToDictionary(o => (o.EmployeeId, o.Date), o => o.ScheduleId));
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
                    scheduleId = rest ? null : planned ?? p.ScheduleId,
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

    /// <summary>Writes a plan: the days and shifts in <paramref name="request"/>, for the people in it.</summary>
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
        if ((long)dates.Count * employeeIds.Count > MaxCells)
            return BadRequest(new { error = "TooManyCells", max = MaxCells });

        // Each step: a shift of this company that is a real template, «rest» or «clear».
        var stepIds = new List<Guid?>();
        var restNeeded = false;
        var shiftIds = new HashSet<Guid>();
        foreach (var step in request.Steps)
        {
            switch (step?.Trim().ToLowerInvariant())
            {
                case "rest":
                    restNeeded = true;
                    stepIds.Add(Guid.Empty);
                    break;
                case "clear":
                    stepIds.Add(null);
                    break;
                default:
                    if (!Guid.TryParse(step, out var id))
                        return BadRequest(new { error = "StepsInvalid" });
                    shiftIds.Add(id);
                    stepIds.Add(id);
                    break;
            }
        }
        if (request.Weekdays is not null && request.RestOnOtherDays)
            restNeeded = true;

        var shifts = await _db.Schedules.Templates().Where(s => shiftIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        if (shifts.Count != shiftIds.Count)
            return BadRequest(new { error = "ScheduleNotFound" });

        var people = await _db.Employees
            .Where(e => employeeIds.Contains(e.Id) && e.IsActive)
            .Select(e => new { e.Id, e.FullName, e.LocationId })
            .ToListAsync(ct);
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

        // What each person's each day becomes: a shift id, Guid.Empty for rest, null to clear, or skipped.
        var plan = new List<(DateOnly Date, Guid? Step)>();
        foreach (var date in dates)
        {
            if (request.Weekdays is int mask && (mask & (1 << (int)date.DayOfWeek)) == 0)
            {
                if (request.RestOnOtherDays)
                    plan.Add((date, Guid.Empty));
                continue;
            }
            var offset = date.DayNumber - anchor.DayNumber;
            plan.Add((date, stepIds[offset % stepIds.Count]));
        }

        var past = plan.Where(p => p.Date < today).Select(p => p.Date).Distinct().Count();
        if (past > 0 && !request.ConfirmPast)
            return Conflict(new { error = "PastDatesNeedConfirm", pastDays = past, people = people.Count });

        Schedule? restDay = null;
        if (restNeeded)
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

        var first = plan.Count > 0 ? plan[0].Date : dates[0];
        var last = plan.Count > 0 ? plan[^1].Date : dates[^1];
        var existing = await _db.ShiftOverrides
            .Where(o => employeeIds.Contains(o.EmployeeId) && o.Date >= first && o.Date <= last)
            .ToDictionaryAsync(o => (o.EmployeeId, o.Date), ct);

        var note = request.Note?.Trim();
        note = string.IsNullOrEmpty(note) ? "Növbə planı" : note[..Math.Min(note.Length, 200)];
        var now = DateTime.UtcNow;
        int set = 0, cleared = 0, unchanged = 0;
        var touched = new HashSet<DateOnly>();
        var perPerson = new Dictionary<Guid, int>();

        foreach (var person in people)
        {
            foreach (var (date, step) in plan)
            {
                existing.TryGetValue((person.Id, date), out var row);
                var target = step == Guid.Empty ? restDay!.Id : step;
                if (target is null)
                {
                    if (row is null) { unchanged++; continue; }
                    _db.ShiftOverrides.Remove(row);
                    cleared++;
                }
                else if (row is null)
                {
                    _db.ShiftOverrides.Add(new ShiftOverride
                    {
                        EmployeeId = person.Id, Date = date, ScheduleId = target.Value, Note = note,
                        CreatedByEmployeeId = me, CreatedAtUtc = now,
                    });
                    set++;
                }
                else if (row.ScheduleId == target.Value)
                {
                    unchanged++;
                    continue;
                }
                else
                {
                    row.ScheduleId = target.Value;
                    row.Note = note;
                    row.CreatedByEmployeeId = me;
                    row.CreatedAtUtc = now;
                    set++;
                }
                touched.Add(date);
                perPerson[person.Id] = perPerson.GetValueOrDefault(person.Id) + 1;
            }
        }

        // One line per person: which days, what they became, who did it — «who moved me to nights?» has an answer.
        var what = string.Join(", ", request.Steps.Select(s => s.Trim().ToLowerInvariant() switch
        {
            "rest" => "İstirahət",
            "clear" => "öz növbəsi",
            _ => Guid.TryParse(s, out var id) && shifts.TryGetValue(id, out var sh) ? sh.Name : s,
        }).Distinct());
        foreach (var (personId, count) in perPerson)
        {
            _db.AuditLogs.Add(new AuditLog
            {
                EmployeeId = personId,
                EventType = AuditEventType.ShiftPlanChanged,
                Reason = $"{first:yyyy-MM-dd}–{last:yyyy-MM-dd}: {count} gün → {what}"
                    + (request.Weekdays is int w ? $" (həftə günləri {w})" : "") + $" · {me}",
            });
        }

        await _db.SaveChangesAsync(ct);

        // Days already worked are rebuilt at once, so the tabel and the pay agree with the plan before anybody
        // looks; today and the days ahead are computed on read.
        var rebuilt = 0;
        foreach (var date in touched.Where(d => d < today).Order())
        {
            await _summaries.GenerateForDateAsync(date, ct);
            rebuilt++;
        }

        return Ok(new { ok = true, set, cleared, unchanged, people = perPerson.Count, rebuiltDays = rebuilt });
    }
}
