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
            })
            .ToListAsync(HttpContext.RequestAborted);
        return Ok(rows);
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
    public async Task<IActionResult> Employees()
    {
        var managed = await ManagedLocationIdsAsync();
        var locationNames = await _db.Locations
            .Where(l => managed.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.Name, HttpContext.RequestAborted);

        var rows = await _db.Employees
            .Where(e => managed.Contains(e.LocationId) && e.Role == EmployeeRole.Employee)
            .OrderBy(e => e.FullName)
            .Select(e => new
            {
                id = e.Id,
                fullName = e.FullName,
                firstName = e.FirstName,
                lastName = e.LastName,
                fatherName = e.FatherName,
                position = e.Position,
                phoneNumber = e.PhoneNumber,
                email = e.Email,
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
                isActive = e.IsActive,
                activated = e.ActivatedAtUtc != null,
            })
            .ToListAsync(HttpContext.RequestAborted);

        return Ok(rows.Select(r => new
        {
            r.id, r.fullName, r.firstName, r.lastName, r.fatherName, r.position, r.phoneNumber, r.email, r.locationId,
            locationName = locationNames.GetValueOrDefault(r.locationId, ""),
            r.birthDate, r.birthYear, r.workStart, r.workEnd, r.photoExempt, r.canFieldCheckIn, r.isActive, r.activated,
            r.scheduleId, r.workCycleDays, r.workCycleOnDays, r.workCycleAnchor,
        }));
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

    // --- leaves -----------------------------------------------------------------

    // GET /api/manager/leaves — leave records for the manager's own staff only. Same boundary as the
    // writes: only Role==Employee. A same-branch admin's or fellow manager's leaves are not the
    // manager's business, and showing rows the manager can no longer delete would only confuse.
    [HttpGet("leaves")]
    public async Task<IActionResult> Leaves([FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
    {
        var ct = HttpContext.RequestAborted;
        var managed = await ManagedLocationIdsAsync();
        var staff = await _db.Employees
            .Where(e => managed.Contains(e.LocationId) && e.Role == EmployeeRole.Employee)
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

    // POST /api/manager/leaves — file a leave for one of the manager's own staff.
    [HttpPost("leaves")]
    public async Task<IActionResult> CreateLeave([FromBody] LeaveRecordRequest request)
    {
        var ct = HttpContext.RequestAborted;
        if (request.ToDate < request.FromDate)
            return BadRequest(new { error = "DateRangeInvalid" });
        if (request.ToDate.DayNumber - request.FromDate.DayNumber + 1 > MaxLeaveRangeDays)
            return BadRequest(new { error = "DateRangeTooLong" });

        // The employee must be one this manager MANAGES — same central rule as the profile edit, so a
        // manager cannot file (and thus alter the paid attendance of) an admin's or a peer's days.
        // Out-of-scope keeps its historical 403 EmployeeNotManaged, which the manager UI translates.
        var (target, denied) = await ManageableEmployeeAsync(
            request.EmployeeId,
            StatusCode(StatusCodes.Status403Forbidden, new { error = "EmployeeNotManaged" }));
        if (target is null)
            return denied!;

        var leave = new LeaveRecord
        {
            EmployeeId = request.EmployeeId,
            FromDate = request.FromDate,
            ToDate = request.ToDate,
            Type = request.Type,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            CreatedByEmployeeId = M15(),
        };
        _db.LeaveRecords.Add(leave);
        await _db.SaveChangesAsync(ct);
        await RecomputeRangeAsync(request.FromDate, request.ToDate);

        return Ok(new { id = leave.Id, employeeId = leave.EmployeeId, fromDate = leave.FromDate, toDate = leave.ToDate, type = leave.Type.ToString() });
    }

    // DELETE /api/manager/leaves/{id} — remove a leave, but only for the manager's own staff.
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
        var (target, denied) = await ManageableEmployeeAsync(
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
        if (!await _db.Schedules.AnyAsync(s => s.Id == id, HttpContext.RequestAborted))
            return "ScheduleNotFound";
        employee.ScheduleId = id;
        return null;
    }
}
