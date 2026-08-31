using System.Security.Cryptography;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Application.Common;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Security;
using AttendanceQR.Infrastructure.Services;
using AttendanceQR.Application.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// The write access a branch Manager has — deliberately its OWN controller, not a widening of the
/// admin one.
///
/// AdminController is a single [Authorize(Roles="Admin")] over a dozen endpoints, several of them
/// company-wide (bulk import, reset-every-photo, delete). Punching manager-sized holes in it means
/// re-gating every method individually and getting all of them right forever; miss one and a manager
/// reaches a company-wide power. Keeping a separate surface means a manager can only ever do what is
/// written HERE, and every method here begins by proving the target sits in a location this manager
/// actually oversees.
///
/// Two things a manager can never do, enforced on every path below rather than trusted to the UI:
/// touch salary (they don't see it and can't set it), or create/promote anyone above Employee (no
/// role escalation). A stale UI cannot get around either — the server re-decides both.
/// </summary>
[ApiController]
[Authorize(Roles = "Manager")]
[Route("api/manager")]
public class ManagerController : ControllerBase
{
    private const int MaxLeaveRangeDays = 366;

    private readonly AppDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IDailySummaryService _dailySummaryService;
    // Platform operators, by employee id — never a manager's to manage, whatever role or branch the
    // row happens to carry.
    private readonly Guid[] _operatorIds;

    public ManagerController(AppDbContext db, IPasswordHasher passwordHasher, IDailySummaryService dailySummaryService,
        AppOptions appOptions)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _dailySummaryService = dailySummaryService;
        _operatorIds = appOptions.SuperAdminIdList();
    }

    private Guid M15() => User.EmployeeId();

    /// <summary>The locations this manager oversees. Everything they may touch is filtered to this set.</summary>
    private Task<List<Guid>> ManagedLocationIdsAsync() =>
        LocationScopeRules.ManagedLocationIdsAsync(_db, M15(), HttpContext.RequestAborted);

    /// <summary>True when the location is one this manager oversees. The gate for creating/moving.</summary>
    private async Task<bool> ManagesLocationAsync(Guid locationId) =>
        (await ManagedLocationIdsAsync()).Contains(locationId);

    /// <summary>
    /// THE authorization decision for a manager acting ON another account — every mutating
    /// per-employee endpoint below (profile edit, PIN reset, leaves) goes through here and nowhere
    /// else, so the rule cannot drift between endpoints.
    ///
    /// A manager may manage someone only if ALL of these hold, decided from the DB row, never from
    /// anything the client sent:
    ///   • same tenant — enforced by the global query filter on Employees (fail-closed);
    ///   • the target sits in a branch this manager oversees (their ManagedLocations set);
    ///   • the target's Role is Employee — NEVER an Admin, another Manager, or the manager themself.
    ///     Admins and managers also have a LocationId (where they clock in), so branch membership
    ///     alone once made them valid targets: a manager could change an admin's email/phone or
    ///     reset their PIN and read the temp PIN — a takeover, and with it role escalation.
    ///
    /// Outside the manager's reach (other branch, other tenant, nonexistent) → <paramref name="outOfScope"/>
    /// (default 404), so a manager cannot even probe for who exists elsewhere. Inside their branch but
    /// wrong role (admin/manager/self) → 403, with no state touched and nothing sensitive returned.
    /// </summary>
    private async Task<(Employee? Employee, IActionResult? Error)> ManageableEmployeeAsync(
        Guid id, IActionResult? outOfScope = null)
    {
        var managed = await ManagedLocationIdsAsync();
        var target = await _db.Employees.FirstOrDefaultAsync(
            e => e.Id == id && managed.Contains(e.LocationId), HttpContext.RequestAborted);
        if (target is null)
            return (null, outOfScope ?? NotFound(new { error = "EmployeeNotFound" }));
        if (target.Role != EmployeeRole.Employee || target.Id == M15())
            return (null, StatusCode(StatusCodes.Status403Forbidden, new { error = "ManagerCannotManageRole" }));
        // A platform operator can sit inside a tenant as an ordinary Employee row. Role and branch
        // would both pass above, and reset-pin hands back the plaintext — so without this a manager
        // could take an operator's credentials. Belt to the same suspenders AdminController wears.
        if (_operatorIds.Contains(target.Id))
            return (null, StatusCode(StatusCodes.Status403Forbidden, new { error = "CannotManageOperator" }));
        return (target, null);
    }

    /// <summary>
    /// Who a LEAVE may be filed for: anyone at this manager's own branches, including a fellow manager
    /// or an admin who works there, plus the manager THEMSELVES.
    ///
    /// Deliberately wider than <see cref="ManageableEmployeeAsync"/>. That gate's Role==Employee rule
    /// exists to stop a manager reaching a PEER's or an ADMIN's ACCOUNT — the branch-only PIN takeover
    /// of 2026-08-08 — and it still guards the only two endpoints that can take an account over: the
    /// employee edit and reset-pin. Filing an absence is not one of them. It turns one day from Qayıb
    /// into İcazə; it touches no PIN, no device binding and no role, every row carries
    /// <c>CreatedByEmployeeId</c>, and an admin can see and delete any of them.
    ///
    /// Applied here, the account rule only produced this: a company with two managers had neither able
    /// to record the other's holiday, so the days counted against them as unexcused absence until an
    /// admin got round to entering it by hand. Self was carved out for exactly that reason; the peer
    /// case is the same problem one person over.
    ///
    /// Platform operators stay out. Their row exists to support the tenant, not to be rostered in it.
    /// </summary>
    private async Task<(Employee? Employee, IActionResult? Error)> LeaveSubjectAsync(
        Guid id, IActionResult outOfScope)
    {
        if (id == M15())
        {
            var self = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id, HttpContext.RequestAborted);
            return self is null ? (null, outOfScope) : (self, null);
        }

        var managed = await ManagedLocationIdsAsync();
        var target = await _db.Employees.FirstOrDefaultAsync(
            e => e.Id == id && managed.Contains(e.LocationId), HttpContext.RequestAborted);
        if (target is null)
            return (null, outOfScope);
        if (_operatorIds.Contains(target.Id))
            return (null, StatusCode(StatusCodes.Status403Forbidden, new { error = "CannotManageOperator" }));
        return (target, null);
    }

    /// <summary>Everyone this manager may file a leave for — the predicate behind both the picker and
    /// the list, kept in one place so the three surfaces cannot drift apart (a name that can be filed
    /// against but not listed reads as a bug, and a row listed but not deletable reads as a worse one).</summary>
    private static IQueryable<Employee> LeaveSubjects(IQueryable<Employee> source, List<Guid> managed, Guid self, Guid[] operatorIds)
        => source.Where(e => (managed.Contains(e.LocationId) && !operatorIds.Contains(e.Id)) || e.Id == self);

    // --- reference data (for the manager's own forms) ---------------------------

    // GET /api/manager/locations — the branches this manager may file against, for dropdowns.
    [HttpGet("locations")]
    public async Task<IActionResult> Locations()
    {
        var managed = await ManagedLocationIdsAsync();
        var rows = await _db.Locations
            .Where(l => managed.Contains(l.Id))
            .OrderBy(l => l.Name)
            .Select(l => new { id = l.Id, name = l.Name })
            .ToListAsync(HttpContext.RequestAborted);
        return Ok(rows);
    }

    // GET /api/manager/positions — the job-title catalogue, read-only (managers pick, admins curate).
    [HttpGet("positions")]
    public async Task<IActionResult> Positions()
    {
        var rows = await _db.JobPositions
            .OrderBy(p => p.Name)
            .Select(p => new { name = p.Name })
            .ToListAsync(HttpContext.RequestAborted);
        return Ok(rows);
    }

    // GET /api/manager/schedules — the named shifts, read-only. A manager assigns people to a shift
    // but does not define them; the hours a company runs on are an admin decision.
    [HttpGet("schedules")]
    public async Task<IActionResult> Schedules()
    {
        var rows = await _db.Schedules
            .OrderBy(sc => sc.Name)
            .Select(sc => new
            {
                id = sc.Id,
                name = sc.Name,
                shiftStart = sc.ShiftStart.ToString("HH:mm"),
                shiftEnd = sc.ShiftEnd.ToString("HH:mm"),
                lateThresholdMinutes = sc.LateThresholdMinutes,
                workDaysMask = sc.WorkDaysMask,
                workCycleDays = sc.WorkCycleDays,
                workCycleOnDays = sc.WorkCycleOnDays,
                workCycleAnchor = sc.WorkCycleAnchor,
                isOvernight = sc.ShiftEnd < sc.ShiftStart,
                // The raw column: DayHours.Parse cannot run inside the SQL projection, so it is
                // turned into the map below, once the rows are in memory.
                dayHoursSpec = sc.DayHours,
            })
            .ToListAsync(HttpContext.RequestAborted);

        // Sent back so the form can round-trip them. Without this the manager's edit screen loads a
        // shift with no per-day hours, and saving it — changing nothing — quietly wipes the ones the
        // admin set.
        var shaped = rows.Select(r => new
        {
            r.id, r.name, r.shiftStart, r.shiftEnd, r.lateThresholdMinutes, r.workDaysMask,
            r.workCycleDays, r.workCycleOnDays, r.workCycleAnchor, r.isOvernight,
            dayHours = DayHours.Parse(r.dayHoursSpec).ToDictionary(
                kv => ((int)kv.Key).ToString(),
                kv => new { start = kv.Value.Start.ToString(@"HH\:mm"), end = kv.Value.End.ToString(@"HH\:mm") }),
        });
        return Ok(shaped);
    }

    // POST /api/manager/schedules — a manager may define a shift. They are the person who knows what
    // hours their crews actually work; the whole reason the old library drifted from reality is that
    // only an admin could correct it.
    //
    // Shifts are company-wide, not branch-scoped, so a new one simply joins the list.
    [HttpPost("schedules")]
    public async Task<IActionResult> CreateSchedule([FromBody] ScheduleRequest request)
    {
        if (!TryParseSchedule(request, out var start, out var end, out var error))
            return BadRequest(new { error });

        var schedule = new Schedule
        {
            Name = request.Name.Trim(),
            ShiftStart = start,
            ShiftEnd = end,
            LateThresholdMinutes = request.LateThresholdMinutes,
            WorkDaysMask = request.WorkDaysMask,
        };
        if (WorkCycle.Apply(schedule, request.WorkCycleDays, request.WorkCycleOnDays, request.WorkCycleAnchor) is { } cycleError)
            return BadRequest(new { error = cycleError });

        if (ScheduleDayHours.Apply(schedule, request.DayHours) is { } dayHoursError)
            return BadRequest(new { error = dayHoursError });

        _db.Schedules.Add(schedule);
        await _db.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(new { id = schedule.Id });
    }

    // PUT /api/manager/schedules/{id} — allowed only while every employee on the shift is one of this
    // manager's own. A shift is shared company-wide, and editing its hours re-judges past days for
    // everyone on it — so changing one that another branch depends on would move somebody else's pay.
    [HttpPut("schedules/{id:guid}")]
    public async Task<IActionResult> UpdateSchedule(Guid id, [FromBody] ScheduleRequest request)
    {
        var ct = HttpContext.RequestAborted;
        var schedule = await _db.Schedules.FirstOrDefaultAsync(sc => sc.Id == id, ct);
        if (schedule is null)
            return NotFound(new { error = "ScheduleNotFound" });
        if (!TryParseSchedule(request, out var start, out var end, out var error))
            return BadRequest(new { error });
        if (await HasOutsideUseAsync(id))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "ScheduleUsedOutsideBranch" });

        schedule.Name = request.Name.Trim();
        schedule.ShiftStart = start;
        schedule.ShiftEnd = end;
        schedule.LateThresholdMinutes = request.LateThresholdMinutes;
        schedule.WorkDaysMask = request.WorkDaysMask;
        if (WorkCycle.Apply(schedule, request.WorkCycleDays, request.WorkCycleOnDays, request.WorkCycleAnchor) is { } cycleError)
            return BadRequest(new { error = cycleError });

        if (ScheduleDayHours.Apply(schedule, request.DayHours) is { } dayHoursError)
            return BadRequest(new { error = dayHoursError });

        await _db.SaveChangesAsync(ct);
        return Ok(new { id = schedule.Id });
    }

    // DELETE /api/manager/schedules/{id} — only while nobody at all is on it. Same rule the admin
    // path uses: without a foreign key, deleting a shift in use drops those employees back to their
    // branch's hours, which changes how their pay is worked out.
    [HttpDelete("schedules/{id:guid}")]
    public async Task<IActionResult> DeleteSchedule(Guid id)
    {
        var ct = HttpContext.RequestAborted;
        var schedule = await _db.Schedules.FirstOrDefaultAsync(sc => sc.Id == id, ct);
        if (schedule is null)
            return NotFound(new { error = "ScheduleNotFound" });

        var assigned = await _db.Employees.CountAsync(e => e.ScheduleId == id, ct);
        if (assigned > 0)
            return Conflict(new { error = "ScheduleInUse", employeeCount = assigned });

        _db.Schedules.Remove(schedule);
        await _db.SaveChangesAsync(ct);
        return Ok(new { deleted = id });
    }

    /// <summary>True when anyone beyond this manager's reach is on the shift — another branch, OR a
    /// same-branch admin/manager. Editing a shift re-judges past days for everyone on it (hours decide
    /// pay), so the same Role==Employee boundary that guards account edits guards this indirect write:
    /// without it a manager could move an admin's pay by editing the shift the admin sits on.</summary>
    private async Task<bool> HasOutsideUseAsync(Guid scheduleId)
    {
        var managed = await ManagedLocationIdsAsync();
        return await _db.Employees.AnyAsync(
            e => e.ScheduleId == scheduleId
                 && !(managed.Contains(e.LocationId) && e.Role == EmployeeRole.Employee),
            HttpContext.RequestAborted);
    }

    /// <summary>Shift-field validation, identical to the admin path's.</summary>
    private static bool TryParseSchedule(ScheduleRequest r, out TimeOnly start, out TimeOnly end, out string? error)
    {
        start = default; end = default; error = null;
        if (string.IsNullOrWhiteSpace(r.Name)) { error = "NameRequired"; return false; }
        if (!TimeOnly.TryParse(r.ShiftStart, out start)) { error = "ShiftStartInvalid"; return false; }
        if (!TimeOnly.TryParse(r.ShiftEnd, out end)) { error = "ShiftEndInvalid"; return false; }
        if (r.LateThresholdMinutes < 0) { error = "LateThresholdNegative"; return false; }
        return true;
    }

    // --- employees --------------------------------------------------------------

    // GET /api/manager/employees — the manager's own branches' staff. No salary field is projected —
    // it is not merely hidden in the UI, it never leaves the server for a manager. Only Role==Employee
    // rows: this list feeds the manager's edit/reset-PIN surface, and admins/managers who merely clock
    // in at the branch are not the manager's to manage (ManageableEmployeeAsync refuses them anyway),
    // so listing them would only leak their contact details behind buttons that 403.
    [HttpGet("employees")]
    public async Task<IActionResult> Employees([FromQuery] bool includeSelf = false)
    {
        // includeSelf is for the LEAVE form only: a manager may file their own absence (see
        // LeaveSubjectAsync), so that one screen needs their own row in the picker. Off by default, and
        // it can only ever add the caller's OWN row — never a peer's.
        var self = M15();
        var managed = await ManagedLocationIdsAsync();
        var locationNames = await _db.Locations
            .Where(l => managed.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.Name, HttpContext.RequestAborted);

        // Everyone at the manager's branches, not just plain staff.
        //
        // It returned Role == Employee only, so a two-manager site showed each of them a roster their
        // colleague was missing from — and the same filter fed the leave picker, which is why filing
        // leave for another manager looked impossible when the endpoint had allowed it all along.
        //
        // Seeing is not acting, and the two boundaries stay apart: this is the SEE rule (their
        // branches, any role), the same one the boards and the profile card already use. What a
        // manager may CHANGE is still decided per write by ManageableEmployeeAsync — their branch AND
        // plain staff — which is the 2026-08-08 line and is untouched here.
        var rows = await _db.Employees
            .Where(e => managed.Contains(e.LocationId) || (includeSelf && e.Id == self))
            .OrderBy(e => e.FullName)
            .Select(e => new
            {
                id = e.Id,
                isSelf = e.Id == self,
                // Whether this row may be acted on — the screen greys out what it cannot change, and
                // the single-card endpoint answers the same question the same way.
                manageable = e.Role == EmployeeRole.Employee && e.Id != self,
                isColleague = e.Role != EmployeeRole.Employee && e.Id != self,
                fullName = e.FullName,
                firstName = e.FirstName,
                lastName = e.LastName,
                fatherName = e.FatherName,
                position = e.Position,
                // A colleague's phone and e-mail are half their login credentials, and the review
                // caught exactly this on the single card in the morning. Widening the roster must not
                // reopen it, so contact details ride only on rows this manager may act on.
                phoneNumber = (e.Role == EmployeeRole.Employee || e.Id == self) ? e.PhoneNumber : null,
                email = (e.Role == EmployeeRole.Employee || e.Id == self) ? e.Email : null,
                locationId = e.LocationId,
                birthDate = e.BirthDate,
                birthYear = e.BirthYear,
                workStart = e.WorkStart == null ? null : e.WorkStart.Value.ToString("HH:mm"),
                workEnd = e.WorkEnd == null ? null : e.WorkEnd.Value.ToString("HH:mm"),
                scheduleId = e.ScheduleId,
                workCycleDays = e.WorkCycleDays,
                workCycleOnDays = e.WorkCycleOnDays,
                workCycleAnchor = e.WorkCycleAnchor,
                photoExempt = e.PhotoExempt,
                canFieldCheckIn = e.CanFieldCheckIn,
                // Not settable through the per-employee edit — ManagerEmployeeRequest carries it but
                // UpdateEmployee never assigns it — and granted only through the bulk action below,
                // which narrows to this manager's own branches' plain staff. Projected because their
                // screen counts and displays it.
                canShareDevice = e.CanShareDevice,
                isActive = e.IsActive,
                activated = e.ActivatedAtUtc != null,
            })
            .ToListAsync(HttpContext.RequestAborted);

        return Ok(rows.Select(r => new
        {
            r.id, r.isSelf, r.manageable, r.isColleague, r.fullName, r.firstName, r.lastName, r.fatherName, r.position, r.phoneNumber, r.email, r.locationId,
            locationName = locationNames.GetValueOrDefault(r.locationId, ""),
            r.birthDate, r.birthYear, r.workStart, r.workEnd, r.photoExempt, r.canFieldCheckIn,
            // Was projected above and then dropped here, so the manager's screen counted zero however
            // many people had it and the "take it back" button computed an empty set and did nothing.
            r.canShareDevice,
            r.isActive, r.activated,
            r.scheduleId, r.workCycleDays, r.workCycleOnDays, r.workCycleAnchor,
        }));
    }

    /// <summary>
    /// ONE employee at one of this manager's branches — what the profile screen needs.
    ///
    /// The admin profile page fetches the whole company and picks the row out of it, which is fine for
    /// an admin and impossible for a manager: that list is unscoped and carries <c>monthlySalary</c>.
    /// This is the same projection as the manager roster, one row, so the profile screen can be shared
    /// without the salary ever leaving the server.
    ///
    /// Uses the SEE boundary, not the ACT one: a manager may open the card of anyone at their branches,
    /// including a peer or an admin who clocks in there — the same rule the boards already follow, and
    /// the reason a two-manager site stopped reading a headcount short. What they may CHANGE is still
    /// decided by <see cref="ManageableEmployeeAsync"/> on each write.
    /// </summary>
    [HttpGet("employees/{id:guid}")]
    public async Task<IActionResult> Employee(Guid id)
    {
        var ct = HttpContext.RequestAborted;
        var managed = await ManagedLocationIdsAsync();

        var e = await _db.Employees
            .FirstOrDefaultAsync(x => x.Id == id && (managed.Contains(x.LocationId) || x.Id == M15()), ct);
        if (e is null)
            return NotFound(new { error = "EmployeeNotFound" });

        var locationName = await _db.Locations
            .Where(l => l.Id == e.LocationId).Select(l => l.Name).FirstOrDefaultAsync(ct) ?? "";

        // WHO may open the card is the SEE boundary — anyone at their branches, so a name on the board
        // is never dead. WHAT the card shows is not the same question.
        //
        // Login here is a phone number or an email plus a four-digit PIN. Handing a manager the
        // telephone number of the admin who clocks in at their branch hands them half of that admin's
        // credentials, which is the reconnaissance half of the 2026-08-08 takeover — the same reason
        // `role` is withheld below. This file already refuses it twice for exactly this reason: the
        // roster filters Role==Employee because "listing them would only leak their contact details
        // behind buttons that 403", and LeaveSubjectList exists as a name-only endpoint on the same
        // argument. A first draft of this card reversed both without noticing.
        var mayAct = e.Role == EmployeeRole.Employee && e.Id != M15() && managed.Contains(e.LocationId);
        var ownContacts = mayAct || e.Id == M15();

        return Ok(new
        {
            id = e.Id,
            isSelf = e.Id == M15(),
            fullName = e.FullName,
            firstName = e.FirstName,
            lastName = e.LastName,
            fatherName = e.FatherName,
            position = e.Position,
            // Null for a peer or an admin: identity, not identifiers.
            phoneNumber = ownContacts ? e.PhoneNumber : null,
            email = ownContacts ? e.Email : null,
            locationId = e.LocationId,
            locationName,
            birthDate = ownContacts ? e.BirthDate : (DateOnly?)null,
            birthYear = ownContacts ? e.BirthYear : null,
            workStart = e.WorkStart?.ToString("HH:mm"),
            workEnd = e.WorkEnd?.ToString("HH:mm"),
            photoExempt = e.PhotoExempt,
            canFieldCheckIn = e.CanFieldCheckIn,
            canShareDevice = e.CanShareDevice,
            isActive = e.IsActive,
            activated = e.ActivatedAtUtc != null,
            scheduleId = e.ScheduleId,
            workCycleDays = e.WorkCycleDays,
            workCycleOnDays = e.WorkCycleOnDays,
            workCycleAnchor = e.WorkCycleAnchor,
            // Deliberately absent: MonthlySalary and Role. Not hidden in the UI — never sent.
            // Whether this manager may ACT on this person is a separate question, answered per write.
            manageable = mayAct,
        });
    }

    // POST /api/manager/employees — add an employee to one of the manager's branches, activated with a
    // temporary PIN they hand over. Role is fixed to Employee and salary is ignored, whatever the body
    // carried.
    [HttpPost("employees")]
    public async Task<IActionResult> CreateEmployee([FromBody] ManagerEmployeeRequest request)
    {
        var ct = HttpContext.RequestAborted;
        if (string.IsNullOrWhiteSpace(request.FullName))
            return BadRequest(new { error = "NameRequired" });
        if (!await ManagesLocationAsync(request.LocationId))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "LocationNotManaged" });

        var phone = PhoneNumbers.Normalize(request.PhoneNumber);
        var hasEmail = !string.IsNullOrWhiteSpace(request.Email);
        if (!hasEmail && phone is null)
            return BadRequest(new { error = "NeedEmailOrPhone" });

        string? email = hasEmail ? request.Email!.Trim() : null;
        if (email is not null && await _db.Employees.AnyAsync(e => e.Email == email, ct))
            return Conflict(new { error = "EmailAlreadyExists" });
        if (phone is not null && await _db.Employees.AnyAsync(e => e.PhoneNumber == phone, ct))
            return Conflict(new { error = "PhoneAlreadyExists" });

        var tempPin = PinRules.Generate();
        var (composedName, firstName, lastName) =
            EmployeeName.Resolve(request.FirstName, request.LastName, request.FullName);
        var employee = new Employee
        {
            FullName = composedName,
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            PhoneNumber = phone,
            FatherName = string.IsNullOrWhiteSpace(request.FatherName) ? null : request.FatherName.Trim(),
            Position = string.IsNullOrWhiteSpace(request.Position) ? null : request.Position.Trim(),
            BirthDate = request.BirthDate,
            BirthYear = request.BirthDate?.Year ?? request.BirthYear,
            LocationId = request.LocationId,
            Role = EmployeeRole.Employee,          // a manager can only ever create an Employee
            PasswordHash = _passwordHasher.Hash(tempPin),
            WorkStart = ParseTimeOrNull(request.WorkStart),
            WorkEnd = ParseTimeOrNull(request.WorkEnd),
            PhotoExempt = request.PhotoExempt,
            CanFieldCheckIn = request.CanFieldCheckIn,
            IsActive = true,
            ActivatedAtUtc = DateTime.UtcNow,      // temp-PIN account — no activation link
            MustChangePin = true,
        };
        if (WorkCycle.Apply(employee, request.WorkCycleDays, request.WorkCycleOnDays, request.WorkCycleAnchor) is { } cycleError)
            return BadRequest(new { error = cycleError });
        if (await ApplyScheduleAsync(employee, request.ScheduleId) is { } scheduleError)
            return BadRequest(new { error = scheduleError });

        _db.Employees.Add(employee);
        await RegisterPositionAsync(employee.Position, ct);
        await _db.SaveChangesAsync(ct);

        return Ok(new { id = employee.Id, tempPin });
    }

    // PUT /api/manager/employees/{id} — edit one of the manager's own staff. Salary, role and any move
    // to a branch they don't oversee are all refused here, not just absent from the form.
    [HttpPut("employees/{id:guid}")]
    public async Task<IActionResult> UpdateEmployee(Guid id, [FromBody] ManagerEmployeeRequest request)
    {
        var ct = HttpContext.RequestAborted;
        var (employee, denied) = await ManageableEmployeeAsync(id);
        if (employee is null)
            return denied!;
        if (string.IsNullOrWhiteSpace(request.FullName))
            return BadRequest(new { error = "NameRequired" });
        // Moving is allowed, but only between branches this same manager oversees.
        if (!await ManagesLocationAsync(request.LocationId))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "LocationNotManaged" });

        var phone = PhoneNumbers.Normalize(request.PhoneNumber);
        var email = string.IsNullOrWhiteSpace(request.Email) ? employee.Email : request.Email.Trim();
        // Guard the uniqueness probe on a non-empty email exactly like the phone one below: with a null
        // email (a phone-only employee), `e.Email == email` translates to `Email IS NULL`, which matches
        // EVERY other phone-only employee and falsely reports "EmailAlreadyExists" — blocking any edit
        // (a shift change, a birthday) for the ~94 accounts that have no email.
        if (!string.IsNullOrWhiteSpace(email) && await _db.Employees.AnyAsync(e => e.Email == email && e.Id != id, ct))
            return Conflict(new { error = "EmailAlreadyExists" });
        if (phone is not null && await _db.Employees.AnyAsync(e => e.PhoneNumber == phone && e.Id != id, ct))
            return Conflict(new { error = "PhoneAlreadyExists" });

        // Deactivating logs the account out (login rejects inactive); bump the token version so the
        // change takes effect immediately, same rule the admin path uses.
        if (employee.IsActive != request.IsActive)
            employee.TokenVersion++;

        (employee.FullName, employee.FirstName, employee.LastName) =
            EmployeeName.Resolve(request.FirstName, request.LastName, request.FullName);
        employee.Email = email;
        employee.PhoneNumber = phone;
        employee.FatherName = string.IsNullOrWhiteSpace(request.FatherName) ? null : request.FatherName.Trim();
        employee.Position = string.IsNullOrWhiteSpace(request.Position) ? null : request.Position.Trim();
        employee.BirthDate = request.BirthDate;
        employee.BirthYear = request.BirthDate?.Year ?? request.BirthYear;
        employee.LocationId = request.LocationId;
        employee.WorkStart = ParseTimeOrNull(request.WorkStart);
        employee.WorkEnd = ParseTimeOrNull(request.WorkEnd);
        employee.PhotoExempt = request.PhotoExempt;
        employee.CanFieldCheckIn = request.CanFieldCheckIn;
        employee.IsActive = request.IsActive;
        if (WorkCycle.Apply(employee, request.WorkCycleDays, request.WorkCycleOnDays, request.WorkCycleAnchor) is { } cycleError)
            return BadRequest(new { error = cycleError });
        if (await ApplyScheduleAsync(employee, request.ScheduleId) is { } scheduleError)
            return BadRequest(new { error = scheduleError });
        // Deliberately NOT touched: Role, MonthlySalary. A manager cannot change either, so the fields
        // are simply never read from the request.
        await RegisterPositionAsync(employee.Position, ct);
        await _db.SaveChangesAsync(ct);

        return Ok(new { id = employee.Id });
    }

    // POST /api/manager/employees/{id}/reset-pin — hand an employee a fresh temporary PIN.
    [HttpPost("employees/{id:guid}/reset-pin")]
    public async Task<IActionResult> ResetPin(Guid id)
    {
        var ct = HttpContext.RequestAborted;
        var (employee, denied) = await ManageableEmployeeAsync(id);
        if (employee is null)
            return denied!;

        var tempPin = PinRules.Generate();
        employee.PasswordHash = _passwordHasher.Hash(tempPin);
        employee.MustChangePin = true;
        employee.TokenVersion++; // any existing session stops working — a reset should end old logins
        await _db.SaveChangesAsync(ct);

        return Ok(new { id = employee.Id, tempPin });
    }

    /// <summary>
    /// Grant or withdraw an opt-in capability across this manager's own staff.
    ///
    /// The manager is the one who knows which of their brigade owns no phone and which of their sites
    /// has no poster, so making them ask an admin for every name is how the permission ends up either
    /// never granted or granted to everyone. The owner asked for this deliberately.
    ///
    /// It is narrower than the admin's in the way that matters: the same
    /// <see cref="ManageableEmployeeAsync"/> boundary as every other manager write. Their own
    /// branches, and Role==Employee only — a manager cannot hand a capability to a fellow manager or
    /// to an admin who clocks in at their site, which is the 2026-08-08 rule and stays.
    ///
    /// Silently skipping anyone out of reach rather than failing the whole call is deliberate too: a
    /// bulk action over a filtered list will sometimes include a name the manager may not act on, and
    /// refusing all of it would teach them to stop using the button.
    /// </summary>
    [HttpPost("employees/bulk-permission")]
    public async Task<IActionResult> BulkGrant([FromBody] BulkPermissionRequest request)
    {
        var ids = (request.EmployeeIds ?? []).Distinct().ToList();
        if (ids.Count == 0)
            return BadRequest(new { error = "NoEmployees" });
        if (ids.Count > 300)
            return BadRequest(new { error = "TooMany" });

        var ct = HttpContext.RequestAborted;
        var managed = await ManagedLocationIdsAsync();
        var targets = await _db.Employees
            .Where(e => ids.Contains(e.Id)
                        && managed.Contains(e.LocationId)
                        && e.Role == EmployeeRole.Employee)
            .ToListAsync(ct);

        var changed = AdminController.ApplyPermission(targets, request.Permission, request.Allowed);
        if (changed > 0)
            await _db.SaveChangesAsync(ct);

        // "skipped" so the screen can say so rather than quietly doing less than it was asked.
        return Ok(new { changed, total = targets.Count, skipped = ids.Count - targets.Count, allowed = request.Allowed });
    }

    // --- leaves -----------------------------------------------------------------

    // GET /api/manager/leaves — leave records for everyone at this manager's branches, plus their own.
    // Exactly the set LeaveSubjectAsync accepts: a row that can be filed but not seen, or seen but not
    // deleted, reads as a broken screen.
    [HttpGet("leaves")]
    public async Task<IActionResult> Leaves([FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
    {
        var ct = HttpContext.RequestAborted;
        var managed = await ManagedLocationIdsAsync();
        var self = M15();
        var staff = await LeaveSubjects(_db.Employees, managed, self, _operatorIds)
            .ToDictionaryAsync(e => e.Id, e => e.FullName, ct);

        var staffIds = staff.Keys.ToList();
        var query = _db.LeaveRecords.Where(l => staffIds.Contains(l.EmployeeId));
        if (from is not null) query = query.Where(l => l.ToDate >= from);
        if (to is not null) query = query.Where(l => l.FromDate <= to);

        var leaves = await query.OrderByDescending(l => l.FromDate).ToListAsync(ct);
        var names = staff;

        return Ok(leaves.Select(l => new
        {
            id = l.Id,
            employeeId = l.EmployeeId,
            employeeName = names.GetValueOrDefault(l.EmployeeId, "—"),
            fromDate = l.FromDate,
            toDate = l.ToDate,
            type = l.Type.ToString(),
            note = l.Note,
        }));
    }

    /// <summary>
    /// The names the leave form may choose from — everyone at this manager's branches, plus themselves.
    ///
    /// A separate endpoint rather than a wider <c>GET employees</c> on purpose. That list feeds the
    /// edit and reset-PIN surface and projects phone, email and birth date; a manager has no business
    /// editing a peer or an admin, so widening it would hand over their contact details to enable a
    /// feature that needs nothing but a name. This returns the name and nothing else that matters.
    /// </summary>
    [HttpGet("leave-subjects")]
    public async Task<IActionResult> LeaveSubjectList()
    {
        var self = M15();
        var managed = await ManagedLocationIdsAsync();
        var rows = await LeaveSubjects(_db.Employees, managed, self, _operatorIds)
            .Where(e => e.IsActive)
            .OrderBy(e => e.FullName)
            .Select(e => new
            {
                id = e.Id,
                fullName = e.FullName,
                position = e.Position,
                isSelf = e.Id == self,
                // So the form can say WHY a name that is not ordinary staff is in the list.
                isColleague = e.Role != EmployeeRole.Employee && e.Id != self,
            })
            .ToListAsync(HttpContext.RequestAborted);

        return Ok(rows);
    }

    // POST /api/manager/leaves — file a leave for anyone at the manager's branches, or for themselves.
    [HttpPost("leaves")]
    public async Task<IActionResult> CreateLeave([FromBody] LeaveRecordRequest request)
    {
        var ct = HttpContext.RequestAborted;
        if (request.ToDate < request.FromDate)
            return BadRequest(new { error = "DateRangeInvalid" });
        if (request.ToDate.DayNumber - request.FromDate.DayNumber + 1 > MaxLeaveRangeDays)
            return BadRequest(new { error = "DateRangeTooLong" });

        var subjects = request.Subjects;
        if (subjects.Count == 0)
            return BadRequest(new { error = "EmployeeNotFound" });

        var existing = await _db.LeaveRecords
            .Where(l => subjects.Contains(l.EmployeeId)
                        && l.FromDate <= request.ToDate && l.ToDate >= request.FromDate)
            .ToListAsync(ct);

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        var created = new List<object>();
        var skipped = new List<object>();

        foreach (var employeeId in subjects)
        {
            // Each row goes through the same central rule as a single one did: a manager cannot file
            // (and so cannot alter the paid attendance of) an admin's or a peer's days. Filing for
            // several people at once must not become a way around it.
            var (target, denied) = await LeaveSubjectAsync(employeeId, null);
            if (target is null)
            {
                skipped.Add(new { employeeId, fullName = "", reason = "EmployeeNotManaged" });
                continue;
            }

            var clash = existing.FirstOrDefault(l =>
                l.EmployeeId == employeeId && LeaveOverlapRule.Overlaps(l, request.FromDate, request.ToDate));
            if (clash is not null)
            {
                skipped.Add(new
                {
                    employeeId,
                    fullName = target.FullName,
                    reason = "Overlaps",
                    conflictType = clash.Type.ToString(),
                    conflictFrom = clash.FromDate,
                    conflictTo = clash.ToDate,
                });
                continue;
            }

            var leave = new LeaveRecord
            {
                EmployeeId = employeeId,
                FromDate = request.FromDate,
                ToDate = request.ToDate,
                Type = request.Type,
                Note = note,
                CreatedByEmployeeId = M15(),
            };
            _db.LeaveRecords.Add(leave);
            created.Add(new { id = leave.Id, employeeId, fullName = target.FullName });
        }

        if (created.Count == 0)
        {
            var onlyScope = skipped.Count > 0 && skipped.All(x =>
                x.GetType().GetProperty("reason")?.GetValue(x)?.ToString() == "EmployeeNotManaged");
            return onlyScope
                ? StatusCode(StatusCodes.Status403Forbidden, new { error = "EmployeeNotManaged" })
                : Conflict(new { error = "AllOverlap", skipped });
        }

        await _db.SaveChangesAsync(ct);
        await RecomputeRangeAsync(request.FromDate, request.ToDate);

        return Ok(new
        {
            created,
            skipped,
            fromDate = request.FromDate,
            toDate = request.ToDate,
            type = request.Type.ToString(),
        });
    }

    // DELETE /api/manager/leaves/{id} — remove a leave of the manager's own staff, or their own.
    [HttpDelete("leaves/{id:guid}")]
    public async Task<IActionResult> DeleteLeave(Guid id)
    {
        var ct = HttpContext.RequestAborted;
        var leave = await _db.LeaveRecords.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (leave is null)
            return NotFound(new { error = "NotFound" });
        // The record exists, but does it belong to someone THIS manager may manage? Out of scope
        // answers as if it doesn't exist rather than confirm a leave belonging to another branch;
        // an admin's/manager's leave in the manager's own branch is refused like every other edit.
        var (target, denied) = await LeaveSubjectAsync(
            leave.EmployeeId, NotFound(new { error = "NotFound" }));
        if (target is null)
            return denied!;

        var (fromDate, toDate) = (leave.FromDate, leave.ToDate);
        _db.LeaveRecords.Remove(leave);
        await _db.SaveChangesAsync(ct);
        await RecomputeRangeAsync(fromDate, toDate);

        return Ok(new { deleted = id });
    }

    // --- helpers ----------------------------------------------------------------

    private async Task RegisterPositionAsync(string? position, CancellationToken ct)
    {
        var name = position?.Trim();
        if (string.IsNullOrEmpty(name)) return;
        if (!await _db.JobPositions.AnyAsync(p => p.Name == name, ct))
            _db.JobPositions.Add(new JobPosition { Name = name });
    }

    private async Task RecomputeRangeAsync(DateOnly from, DateOnly to)
    {
        for (var date = from; date <= to; date = date.AddDays(1))
            await _dailySummaryService.GenerateForDateAsync(date, HttpContext.RequestAborted);
    }

    private static TimeOnly? ParseTimeOrNull(string? value) =>
        TimeOnly.TryParse(value, out var t) ? t : null;

    /// <summary>
    /// Assigns (or clears) the employee's named shift. Returns an error code, or null on success.
    ///
    /// A shift from another company would be an outright tenant leak, so the id is verified against
    /// the query-filtered set rather than trusted from the body.
    /// </summary>
    private async Task<string?> ApplyScheduleAsync(Employee employee, Guid? scheduleId)
    {
        if (scheduleId is not Guid id)
        {
            employee.ScheduleId = null;
            return null;
        }
        var schedule = await _db.Schedules
            .Where(s => s.Id == id)
            .Select(s => new { s.LocationId })
            .FirstOrDefaultAsync(HttpContext.RequestAborted);
        if (schedule is null)
            return "ScheduleNotFound";
        // A shift pinned to a branch is offered only to that branch's staff — see ScheduleAssignmentRule,
        // which the manager path shares so the two can never drift apart.
        if (ScheduleAssignmentRule.Refusal(schedule.LocationId, employee.LocationId) is { } branchRefusal)
            return branchRefusal;
        employee.ScheduleId = id;
        return null;
    }
}
