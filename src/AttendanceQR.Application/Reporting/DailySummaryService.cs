using AttendanceQR.Application.Common;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Application.Reporting;

public interface IDailySummaryService
{
    /// <summary>
    /// (Re)computes the day's summary for every active, activated employee. Idempotent — existing
    /// rows for the date are updated in place, so re-running never creates duplicates. Returns the
    /// number of employees summarized.
    /// </summary>
    Task<int> GenerateForDateAsync(DateOnly date, CancellationToken ct = default);
}

public sealed class DailySummaryService : IDailySummaryService
{
    private readonly AppDbContext _db;
    private readonly TimeZoneInfo _timeZone;
    private readonly string[] _hiddenEmails;

    public DailySummaryService(AppDbContext db, AppOptions options)
    {
        _db = db;
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);
        _hiddenEmails = options.HiddenEmailList();
    }

    public async Task<int> GenerateForDateAsync(DateOnly date, CancellationToken ct = default)
    {
        // A DailySummary is a FINISHED-day record. Never generate today or a future date: today is always
        // computed live (the board and reports never read today from here), and a future day has no
        // records yet — so it would mark every employee Absent. This used to happen wholesale: a leave
        // ending weeks out made RecomputeRange call this for every future day, pre-creating Absent rows
        // for all staff up to the leave's end. Guard it here, at the one place, so no caller can.
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone));
        if (date >= today)
            return 0;

        // Only the system/root accounts in HiddenEmails are excluded; admins/managers who also clock in
        // (e.g. a director who scans) get summarised like any staff (mirrors the live "today" board).
        var employees = await _db.Employees
            .Where(e => e.IsActive && e.ActivatedAtUtc != null && (e.Email == null || !_hiddenEmails.Contains(e.Email.ToLower())))
            .Select(e => new { e.Id, e.LocationId, e.ScheduleId, e.WorkStart, e.WorkEnd, e.WorkCycleDays, e.WorkCycleOnDays, e.WorkCycleAnchor, e.ActivatedAtUtc })
            .ToListAsync(ct);

        // A handful of rows per tenant, so they are loaded whole and looked up in memory rather
        // than joined into every employee projection.
        var schedules = await _db.Schedules.ToDictionaryAsync(sc => sc.Id, ct);
        // «Əvəzləmə» for this one date: whoever worked somebody else's shift is judged by THAT shift,
        // which is what lets a cover night be an overnight at all.
        var overrides = new ShiftOverrideMap(await _db.ShiftOverrides
            .Where(o => o.Date == date)
            .ToDictionaryAsync(o => (o.EmployeeId, o.Date), o => o.ScheduleId, ct));

        // «Qayıb yaz» for this date: the people a manager has stated did not come. Absence is no longer
        // inferred for anyone who has never recorded any attendance (see IsStillOnboarding), so this
        // is how such a day gets its Qayıb — from somebody who watched it happen.
        var absenceMarks = await _db.AbsenceMarks
            .Where(a => a.Date == date)
            .Select(a => a.EmployeeId)
            .ToListAsync(ct);
        var marked = absenceMarks.ToHashSet();

        var locationIds = employees.Select(e => e.LocationId).Distinct().ToList();
        var locations = await _db.Locations
            .Where(l => locationIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, ct);

        // A voided record is not a day. It stays in the table with its selfie — that photograph is
        // the evidence behind the disciplinary action — but every computation of what a day WAS has
        // to skip it, or the fraudulent scan keeps paying for itself. Filtered at the load, in both
        // the nightly path and the live one, so the stored summary and today's board agree.
        // Grouped, not keyed. A day used to be one row per person — the database enforced it — and a
        // dictionary keyed on EmployeeId was therefore safe. It is not any more: a split shift works
        // one day in two stretches, and this load would have thrown on the duplicate key the first
        // night the «əlavə qüvvə» crew came back at ten. The FIRST block stays the day's record — it
        // carries the arrival, the selfie, the lateness — and the rest are folded in as extra spans,
        // exactly the way field visits already are.
        var recordRows = await _db.AttendanceRecords
            .Where(r => r.VoidedAtUtc == null)
            .Where(r => r.AttendanceDate == date)
            .ToListAsync(ct);
        var blocksByEmployee = recordRows
            .GroupBy(r => r.EmployeeId)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.CheckInAtUtc ?? DateTime.MaxValue).ToList());
        var records = blocksByEmployee.ToDictionary(kv => kv.Key, kv => kv.Value[0]);

        // Field visits are attendance too. A worker sent to a site with no QR poster proves presence
        // with GPS + time instead of a scan, so a day spent in the field must count as WORKED here —
        // this table is what the tabel and the payroll deduction read. Without it a driver who spent
        // the whole month on field visits reads as Qayıb every day and loses a month's pay.
        // Aggregated per employee: earliest arrival, latest departure, and whether any visit is still
        // open (that day is Incomplete, exactly like a forgotten office check-out).
        // Loaded as individual visits rather than as min/max bounds: two twenty-minute visits hours
        // apart are two stretches, and measuring them first-arrival-to-last-departure would pay the
        // whole afternoon between them. The grouping happens in memory, where the span merge lives.
        var fieldRaw = await _db.FieldVisits
            .Where(v => v.VisitDate == date && v.Status != FieldVisitStatus.Cancelled && v.CheckInAtUtc != null)
            .Select(v => new { v.EmployeeId, v.CheckInAtUtc, v.CheckOutAtUtc })
            .ToListAsync(ct);
        var fieldByEmployee = fieldRaw
            .GroupBy(v => v.EmployeeId)
            .ToDictionary(g => g.Key, g => new
            {
                FirstIn = g.Min(v => v.CheckInAtUtc),
                LastOut = g.Max(v => v.CheckOutAtUtc),
                AnyOpen = g.Any(v => v.CheckOutAtUtc == null),
                Spans = g.Where(v => v.CheckOutAtUtc != null)
                    .Select(v => new AttendanceCalculator.WorkSpan(v.CheckInAtUtc!.Value, v.CheckOutAtUtc!.Value))
                    .ToList(),
            });

        // Admin-declared non-working days for this date: either global (LocationId == null) or
        // specific to one of the locations in play. A location is "off" if a matching row exists.
        var nonWorkingLocationIds = await _db.NonWorkingDays
            .Where(n => n.Date == date && (n.LocationId == null || locationIds.Contains(n.LocationId.Value)))
            .Select(n => n.LocationId)
            .ToListAsync(ct);
        var isGloballyNonWorking = nonWorkingLocationIds.Contains(null);
        var nonWorkingLocationIdSet = nonWorkingLocationIds
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToHashSet();

        // Approved leave/permission covering this date, per employee — takes priority over both
        // Absent and DayOff (defensive GroupBy/First in case an admin ever creates overlapping
        // ranges for the same employee).
        var employeeIds = employees.Select(e => e.Id).ToList();
        var leaveByEmployee = await _db.LeaveRecords
            .Where(l => l.FromDate <= date && l.ToDate >= date && employeeIds.Contains(l.EmployeeId))
            .GroupBy(l => l.EmployeeId)
            .Select(g => new { EmployeeId = g.Key, Type = g.First().Type })
            .ToDictionaryAsync(x => x.EmployeeId, x => x.Type, ct);

        // Existing summaries for the date → upsert (idempotent; the unique index also guards this).
        var existing = await _db.DailySummaries
            .Where(s => s.SummaryDate == date)
            .ToDictionaryAsync(s => s.EmployeeId, ct);

        // The earliest attendance each employee has EVER recorded — office scan or field visit. It is
        // what tells an onboarding apart from an absence: see AttendanceCalculator.IsStillOnboarding.
        // One grouped query per run; nothing here is per-employee.
        var firstScan = await _db.AttendanceRecords
            .Where(r => r.CheckInAtUtc != null)
            .GroupBy(r => r.EmployeeId)
            .Select(g => new { EmployeeId = g.Key, First = g.Min(r => r.AttendanceDate) })
            .ToDictionaryAsync(x => x.EmployeeId, x => x.First, ct);
        var firstField = await _db.FieldVisits
            .Where(v => v.CheckInAtUtc != null && v.Status != FieldVisitStatus.Cancelled)
            .GroupBy(v => v.EmployeeId)
            .Select(g => new { EmployeeId = g.Key, First = g.Min(v => v.VisitDate) })
            .ToDictionaryAsync(x => x.EmployeeId, x => x.First, ct);

        DateOnly? FirstAttendanceOf(Guid employeeId)
        {
            var a = firstScan.TryGetValue(employeeId, out var s1) ? s1 : (DateOnly?)null;
            var b = firstField.TryGetValue(employeeId, out var s2) ? s2 : (DateOnly?)null;
            if (a is null) return b;
            if (b is null) return a;
            return a < b ? a : b;
        }

        foreach (var emp in employees)
        {
            if (!locations.TryGetValue(emp.LocationId, out var location))
                continue; // defensive: employee's location vanished

            // Not yet onboarded on this date: nobody can scan before they activate, so a working day
            // before activation is not an absence — it's a day the person didn't exist here. Skip it,
            // so a mid-month hire isn't billed a string of phantom Qayıb days before they started.
            if (emp.ActivatedAtUtc is DateTime act
                && DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(act, _timeZone)) > date)
                continue;

            // Activated, but not yet actually up and running: bulk import stamps ActivatedAtUtc the
            // moment the spreadsheet is pasted, so without this every day between the import and the
            // person's first working scan is written as Qayıb — and payroll deducts a day per Qayıb.
            // 876 such days appeared in one company's first week. Skipped, not stored: the same
            // treatment as a day before the account existed, because that is what it is.
            // Two things outrank the onboarding rule, and both are somebody's decision rather than an
            // inference:
            //   • a manager's «Qayıb yaz» — precisely the case this rule exists to answer;
            //   • an approved LEAVE. Skipping the day here threw the leave away: sixteen people at
            //     Bakı Abadlıq Xidməti had holidays, sick leave and a work trip entered by their
            //     managers and NOTHING in the tabel for any of it — Abdullayev Elnur's eleven days of
            //     sick leave, Kərimova Arzu's fifteen days of holiday, all of it silently absent from
            //     the timesheet the accountant works from. The onboarding rule exists to stop a
            //     silence being read as absence; it was never meant to erase a decision.
            if (!marked.Contains(emp.Id)
                && !leaveByEmployee.ContainsKey(emp.Id)
                && AttendanceCalculator.IsStillOnboarding(
                    date, emp.ActivatedAtUtc, FirstAttendanceOf(emp.Id), _timeZone))
            {
                // Not merely "don't write one" — REMOVE the row if a previous run already wrote it.
                // This job upserts, so skipping alone would leave every Qayıb day already on the
                // books exactly where it was, and re-running would change nothing. The 876 rows this
                // rule exists to undo were written before the rule existed; clearing them is the
                // whole point. Safe because a DailySummary is derived data — it is rebuilt from
                // AttendanceRecords on demand, and this same method is what rebuilds it.
                if (existing.TryGetValue(emp.Id, out var stale))
                    _db.DailySummaries.Remove(stale);
                continue;
            }

            var shift = EffectiveShift.Resolve(
                emp.WorkStart, emp.WorkEnd, emp.WorkCycleDays, emp.WorkCycleOnDays, emp.WorkCycleAnchor,
                overrides.ScheduleFor(emp.Id, date, emp.ScheduleId, schedules), location);

            var isWorkingDay = shift.IsWorkingDay(date)
                                && !isGloballyNonWorking
                                && !nonWorkingLocationIdSet.Contains(location.Id);
            LeaveType? leaveType = leaveByEmployee.TryGetValue(emp.Id, out var lt) ? lt : null;
            var noRecordStatus = AttendanceCalculator.ResolveNoRecordStatus(isWorkingDay, leaveType);

            records.TryGetValue(emp.Id, out var record);

            // The manager's word, applied. It is deliberately the WEAKEST of the three: a scan or an
            // approved leave is evidence and this is testimony, so if either turns up afterwards the
            // mark goes quiet rather than overruling it. The write path refuses a mark on a day that
            // already has one, so in practice this only settles what happens when somebody adds leave
            // to a day that was already marked — and there, the leave is the later decision.
            if (marked.Contains(emp.Id) && record is null && leaveType is null)
                noRecordStatus = DailySummaryStatus.Absent;

            // An office record always wins: someone who scanned at their branch is judged by that scan,
            // and folding field minutes into it would double-count overlapping time. Field visits only
            // fill a day that has NO scan — the same rule the live "today" board uses. Note this
            // overrides DayOff/OnLeave too: turning up to a site on a rest day or mid-vacation is worked
            // time, exactly as the LeaveRecord rule already says for a scan on a leave day.
            var fieldRecord = record?.CheckInAtUtc is null && fieldByEmployee.TryGetValue(emp.Id, out var fv)
                ? new AttendanceRecord
                {
                    EmployeeId = emp.Id,
                    LocationId = emp.LocationId,
                    AttendanceDate = date,
                    CheckInAtUtc = fv.FirstIn,
                    // Any still-open visit leaves the day open, whatever the other visits did.
                    CheckOutAtUtc = fv.AnyOpen ? null : fv.LastOut,
                }
                : null;

            var computed = Compute(emp.Id, emp.LocationId, date, record ?? fieldRecord, shift, isWorkingDay, noRecordStatus);

            // Minutes across EVERY stretch of the day, not just the pair Compute was handed. Two cases
            // reach this, and both used to be measured wrong:
            //   • field-only, several visits — measured first-arrival-to-last-departure, which paid
            //     the whole gap between two twenty-minute visits;
            //   • MIXED — a site in the morning and an office scan in the afternoon, where the office
            //     scan won outright and the field hours vanished from the tabel.
            // The union counts overlap once and pays each gap up to the travel cap. Status, lateness
            // and overtime are left exactly as computed above: those are judged against the shift, and
            // only the office half has an hour it was due at.
            // The day's FURTHER office blocks join the field visits here, as stretches of the same
            // kind: a split day's night is presence that is not the first pair, exactly like a visit.
            var extraSpans = new List<AttendanceCalculator.WorkSpan>();
            var anyExtraOpen = false;
            if (fieldByEmployee.TryGetValue(emp.Id, out var fvSpans))
            {
                extraSpans.AddRange(fvSpans.Spans);
                anyExtraOpen = fvSpans.AnyOpen;
            }
            if (blocksByEmployee.TryGetValue(emp.Id, out var blocks) && blocks.Count > 1)
            {
                foreach (var extra in blocks.Skip(1))
                {
                    if (extra.CheckInAtUtc is DateTime bIn && extra.CheckOutAtUtc is DateTime bOut)
                        extraSpans.Add(new AttendanceCalculator.WorkSpan(bIn, bOut));
                    else
                        anyExtraOpen = true;
                }
            }
            if (extraSpans.Count > 0 || anyExtraOpen)
            {
                var merged = AttendanceCalculator.MergedWorkedMinutes(record, extraSpans, anyExtraOpen);
                if (merged is int minutes)
                    computed.WorkedMinutes = minutes;
            }

            // A field visit has no fixed arrival to be late for — the manager decides when they are
            // sent and the travel is not theirs to control. So a field-derived day never carries
            // lateness; "Late" would invent a fault out of a dispatch decision. (It changes nothing
            // the tabel shows — Late and OnTime are both "İ" — but it keeps the stored record honest.)
            if (fieldRecord is not null)
            {
                if (computed.Status == DailySummaryStatus.Late)
                    computed.Status = DailySummaryStatus.OnTime;
                computed.LateMinutes = 0;
            }

            if (existing.TryGetValue(emp.Id, out var summary))
            {
                summary.LocationId = computed.LocationId;
                summary.CheckInAtUtc = computed.CheckInAtUtc;
                summary.CheckOutAtUtc = computed.CheckOutAtUtc;
                summary.WorkedMinutes = computed.WorkedMinutes;
                summary.Status = computed.Status;
                summary.LateMinutes = computed.LateMinutes;
                summary.OvertimeMinutes = computed.OvertimeMinutes;
                summary.EarlyLeaveMinutes = computed.EarlyLeaveMinutes;
                summary.EarlyArriveMinutes = computed.EarlyArriveMinutes;
            }
            else
            {
                _db.DailySummaries.Add(computed);
            }
        }

        await _db.SaveChangesAsync(ct);
        return employees.Count;
    }

    private DailySummary Compute(
        Guid employeeId, Guid locationId, DateOnly date, AttendanceRecord? record, EffectiveShift shift,
        bool isWorkingDay, DailySummaryStatus noRecordStatus)
    {
        // Shared timezone/late/overtime logic (also used by the live "today" query), against the shift
        // already resolved for this employee — the same one the scan endpoint judged them by.
        var c = AttendanceCalculator.Compute(record, shift, _timeZone, isWorkingDay, noRecordStatus);
        return new DailySummary
        {
            EmployeeId = employeeId,
            LocationId = locationId,
            SummaryDate = date,
            CheckInAtUtc = record?.CheckInAtUtc,
            CheckOutAtUtc = record?.CheckOutAtUtc,
            Status = c.Status,
            WorkedMinutes = c.WorkedMinutes,
            LateMinutes = c.LateMinutes,
            OvertimeMinutes = c.OvertimeMinutes,
            EarlyLeaveMinutes = c.EarlyLeaveMinutes,
            EarlyArriveMinutes = c.EarlyArriveMinutes,
        };
    }
}
