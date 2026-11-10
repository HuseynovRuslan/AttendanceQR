using AttendanceQR.Api.Contracts;
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
/// The manager's side of presence checks («Mövcudluq yoxlaması», see <see cref="PresenceCheck"/>): switch them on
/// for one person they have a reason to doubt, for 7, 14 or 30 days, and read what came back.
///
/// «attendance.fraud» — the right that already covers voiding a faked day and warning about a photo. Reading the
/// history follows who may see the person (a manager sees everyone at their branches); switching it on follows who
/// may act on them (never an admin, never oneself, only at one's own branches) — the same two boundaries as every
/// other per-person action.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin,Manager")]
[Route("api/admin/employees/{employeeId:guid}/presence-checks")]
public class AdminPresenceChecksController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;

    public AdminPresenceChecksController(AppDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    // GET /api/admin/employees/{id}/presence-checks — the switch, the last week in one line, and the history.
    [Permission(AccessPermissions.AttendanceFraud)]
    [HttpGet]
    public async Task<IActionResult> Get(Guid employeeId)
    {
        var ct = HttpContext.RequestAborted;
        if (!await LocationScopeRules.CanAccessEmployeeAsync(
                _db, User.EmployeeId(), User.Role(), employeeId, ct, AccessPermissions.AttendanceFraud))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "OutOfScope" });

        var employee = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        if (employee is null)
            return NotFound(new { error = "EmployeeNotFound" });

        return Ok(await ViewAsync(employee, ct));
    }

    // PUT /api/admin/employees/{id}/presence-checks — { days: 7 | 14 | 30 } on, { days: null } off.
    [Permission(AccessPermissions.AttendanceFraud)]
    [HttpPut]
    public async Task<IActionResult> Set(Guid employeeId, [FromBody] PresenceCheckSettingRequest request)
    {
        var ct = HttpContext.RequestAborted;
        var me = User.EmployeeId();
        if (!await LocationScopeRules.CanManageEmployeeAsync(_db, me, User.Role(), employeeId, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "OutOfScope" });

        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        if (employee is null)
            return NotFound(new { error = "EmployeeNotFound" });

        var now = _clock.GetUtcNow().UtcDateTime;
        var days = request.Days ?? 0;
        string reason;
        if (days == 0)
        {
            employee.PresenceCheckUntilUtc = null;
            // Off means off now, not after the checks already drawn for today have gone out.
            var planned = await _db.PresenceChecks
                .Where(c => c.EmployeeId == employeeId && c.Result == PresenceCheckResult.Pending && c.SentAtUtc == null)
                .ToListAsync(ct);
            foreach (var check in planned)
            {
                check.Result = PresenceCheckResult.Cancelled;
                check.Note = PresenceCheckRules.NoteDisabled;
            }
            reason = $"off by {me}";
        }
        else
        {
            if (!PresenceCheckRules.AllowedDays.Contains(days))
                return BadRequest(new { error = "InvalidDuration" });
            if (!employee.IsActive)
                return Conflict(new { error = "EmployeeInactive" });
            employee.PresenceCheckUntilUtc = now.AddDays(days);
            employee.PresenceCheckSetByEmployeeId = me;
            employee.PresenceCheckSetAtUtc = now;
            reason = $"on {days}d until {employee.PresenceCheckUntilUtc:yyyy-MM-dd HH:mm}Z by {me}";
        }

        _db.AuditLogs.Add(new AuditLog
        {
            EmployeeId = employeeId,
            EventType = AuditEventType.PresenceCheckSettingChanged,
            Reason = reason,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
        });
        await _db.SaveChangesAsync(ct);

        return Ok(await ViewAsync(employee, ct));
    }

    private async Task<object> ViewAsync(Employee employee, CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var on = PresenceCheckRules.IsOn(employee.PresenceCheckUntilUtc, now);
        var setBy = on && employee.PresenceCheckSetByEmployeeId is Guid by
            ? await _db.Employees.AsNoTracking().Where(e => e.Id == by).Select(e => e.FullName).FirstOrDefaultAsync(ct)
            : null;

        // Only the checks that were actually asked. The planned moments are never shown — a manager who can read
        // them can be asked for them, and a check that is known in advance checks nothing.
        var asked = await (
            from c in _db.PresenceChecks
            where c.EmployeeId == employee.Id && c.SentAtUtc != null && c.Result != PresenceCheckResult.Cancelled
            join l in _db.Locations on c.LocationId equals l.Id into ls
            from l in ls.DefaultIfEmpty()
            orderby c.SentAtUtc descending
            select new
            {
                id = c.Id,
                sentAtUtc = c.SentAtUtc,
                result = c.Result.ToString(),
                respondedAtUtc = c.RespondedAtUtc,
                late = c.Late,
                distanceMeters = c.DistanceMeters,
                radiusMeters = c.RadiusMeters,
                accuracyMeters = c.AccuracyMeters,
                reached = c.Reached,
                branch = l == null ? null : l.Name,
            })
            .Take(60)
            .ToListAsync(ct);

        var weekAgo = now.AddDays(-7);
        var week = asked.Where(c => c.sentAtUtc >= weekAgo).ToList();

        return new
        {
            on,
            untilUtc = on ? employee.PresenceCheckUntilUtc : null,
            setAtUtc = on ? employee.PresenceCheckSetAtUtc : null,
            setByName = setBy,
            canChange = await LocationScopeRules.CanManageEmployeeAsync(_db, User.EmployeeId(), User.Role(), employee.Id, ct),
            allowedDays = PresenceCheckRules.AllowedDays,
            week = new
            {
                asked = week.Count,
                inside = week.Count(c => c.result == nameof(PresenceCheckResult.Inside)),
                outside = week.Count(c => c.result == nameof(PresenceCheckResult.Outside)),
                noAnswer = week.Count(c => c.result == nameof(PresenceCheckResult.NoAnswer)),
                gpsFailed = week.Count(c => c.result == nameof(PresenceCheckResult.GpsFailed)),
                late = week.Count(c => c.late),
            },
            checks = asked,
        };
    }
}
