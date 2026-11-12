using AttendanceQR.Api.Contracts;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api;

/// <summary>
/// What adding one person to a branch from the manager's form means: a name, a phone number or an e-mail that
/// nobody in the company already logs in with, a temporary PIN, plain staff and nothing above it, and a shift the
/// branch may use.
///
/// There is one copy of these rules, and two ways in. The manager desk (POST /api/manager/employees) adds the
/// person at once. An approved «Yeni işçi» request (ChangeRequestDesk) adds them when the operator says yes. Both
/// call this, so a request can never create an account the desk would have refused, and the desk's rules cannot
/// change without the requests following.
///
/// Who may add somebody, and where, is NOT here. Each caller proves that first.
/// </summary>
public static class NewEmployee
{
    public sealed record Ready(Employee Employee, string TempPin);

    /// <summary>
    /// Builds the account the request describes, without adding it anywhere. Returns either the account and its
    /// temporary PIN, or the refusal code the manager desk has always answered with. <paramref name="Conflict"/>
    /// says the refusal is «somebody already has this», which the desk answers with 409 rather than 400.
    /// </summary>
    public static async Task<(Ready? Ready, string? Error, bool Conflict)> PrepareAsync(
        AppDbContext db, IPasswordHasher hasher, ManagerEmployeeRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.FullName))
            return (null, "NameRequired", false);

        var phone = PhoneNumbers.Normalize(request.PhoneNumber);
        var hasEmail = !string.IsNullOrWhiteSpace(request.Email);
        if (!hasEmail && phone is null)
            return (null, "NeedEmailOrPhone", false);

        string? email = hasEmail ? request.Email!.Trim() : null;
        if (email is not null && await db.Employees.AnyAsync(e => e.Email == email, ct))
            return (null, "EmailAlreadyExists", true);
        if (phone is not null && await db.Employees.AnyAsync(e => e.PhoneNumber == phone, ct))
            return (null, "PhoneAlreadyExists", true);

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
            Role = EmployeeRole.Employee,          // this path only ever creates plain staff
            PasswordHash = hasher.Hash(tempPin),
            WorkStart = TimeOnly.TryParse(request.WorkStart, out var start) ? start : null,
            WorkEnd = TimeOnly.TryParse(request.WorkEnd, out var end) ? end : null,
            PhotoExempt = request.PhotoExempt,
            CanFieldCheckIn = request.CanFieldCheckIn,
            QrlessCheckInOverride = request.QrlessCheckInOverride,
            RequireGeofenceOverride = request.RequireGeofenceOverride,
            IsActive = true,
            ActivatedAtUtc = DateTime.UtcNow,      // temp-PIN account, so there is no activation link
            MustChangePin = true,
        };
        if (WorkCycle.Apply(employee, request.WorkCycleDays, request.WorkCycleOnDays, request.WorkCycleAnchor) is { } cycleError)
            return (null, cycleError, false);
        if (await ApplyScheduleAsync(db, employee, request.ScheduleId, ct) is { } scheduleError)
            return (null, scheduleError, false);
        return (new Ready(employee, tempPin), null, false);
    }

    /// <summary>Puts a prepared account into the context, with its job title in the catalogue. The caller saves.</summary>
    public static async Task AddAsync(AppDbContext db, Employee employee, CancellationToken ct)
    {
        db.Employees.Add(employee);
        await RegisterPositionAsync(db, employee.Position, ct);
    }

    /// <summary>A job title the catalogue does not have yet is added to it, so the next person picks it from the list.</summary>
    public static async Task RegisterPositionAsync(AppDbContext db, string? position, CancellationToken ct)
    {
        var name = position?.Trim();
        if (string.IsNullOrEmpty(name)) return;
        if (!await db.JobPositions.AnyAsync(p => p.Name == name, ct)
            && !db.JobPositions.Local.Any(p => p.Name == name))
            db.JobPositions.Add(new JobPosition { Name = name });
    }

    /// <summary>
    /// Assigns (or clears) the employee's named shift. Returns an error code, or null on success.
    ///
    /// A shift from another company would be an outright tenant leak, so the id is checked against the
    /// query-filtered set rather than trusted from the body.
    /// </summary>
    public static async Task<string?> ApplyScheduleAsync(AppDbContext db, Employee employee, Guid? scheduleId, CancellationToken ct, Guid? actorId = null)
    {
        if (scheduleId is not Guid id)
        {
            await StandingShifts.SetFromFormAsync(db, employee, null, actorId, ct);
            return null;
        }
        var schedule = await db.Schedules.Templates()
            .Where(s => s.Id == id)
            .Select(s => new { s.LocationId })
            .FirstOrDefaultAsync(ct);
        if (schedule is null)
            return "ScheduleNotFound";
        // A shift pinned to a branch is offered only to that branch's staff. See ScheduleAssignmentRule, which
        // the admin path shares, so the two can never drift apart.
        if (ScheduleAssignmentRule.Refusal(schedule.LocationId, employee.LocationId) is { } branchRefusal)
            return branchRefusal;
        // From today on: the days before keep the shift they were worked on (StandingShifts).
        await StandingShifts.SetFromFormAsync(db, employee, id, actorId, ct);
        return null;
    }
}
