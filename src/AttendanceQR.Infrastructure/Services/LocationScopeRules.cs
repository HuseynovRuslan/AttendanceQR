using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Infrastructure.Services;

/// <summary>
/// Single source of truth for location-based visibility (manager scope).
/// <para>
/// It lives in Infrastructure — not next to the Application-layer report scope — because
/// Infrastructure services (e.g. <see cref="AttendanceQueryService"/>) cannot reference Application,
/// whereas Application can reference Infrastructure. Putting the rule here lets BOTH the attendance
/// query (Infrastructure) and the report/export scope (Application) derive manager scope from one place.
/// A manager is scoped by their <c>ManagedLocations</c> set; their own <c>Employee.LocationId</c> is
/// never used for authorization.
/// </para>
/// </summary>
public static class LocationScopeRules
{
    /// <summary>The locations a manager oversees (their ManagedLocations set) — in a company switched to
    /// Enforced, the branches the caller holds «employees.view» over, and every right of the request too (see
    /// IAuthzObserver.RequestPermissions).</summary>
    public static async Task<List<Guid>> ManagedLocationIdsAsync(AppDbContext db, Guid managerId, CancellationToken ct)
    {
        var managed = await LegacyManagedAsync(db, managerId, ct);
        if (db.AuthzObserver is { } observer)
            return (await observer.DecideManagedSetAsync(db, managerId, EmployeeRole.Manager, managed, ct)).ToList();
        return managed;
    }

    /// <summary>
    /// Where <paramref name="callerId"/> may use <paramref name="permission"/> — the whole company, some branches,
    /// or nowhere. What every list and board narrows itself by. Today: an admin everywhere, a manager over their
    /// ManagedLocations, anybody else nowhere; in a company switched to Enforced, the permission model.
    /// </summary>
    public static async Task<Access.ScopeAnswer> ScopeAsync(
        AppDbContext db, Guid callerId, EmployeeRole role, string permission, CancellationToken ct)
    {
        if (db.AuthzObserver is { } observer)
            return await observer.ScopeAsync(db, callerId, role, permission, ct);
        return role switch
        {
            EmployeeRole.Admin => Access.ScopeAnswer.Everywhere,
            EmployeeRole.Manager => new Access.ScopeAnswer(false, (await LegacyManagedAsync(db, callerId, ct)).ToHashSet()),
            _ => Access.ScopeAnswer.Nowhere,
        };
    }

    /// <summary>
    /// The role today's code paths should take for this caller and this permission. In Legacy and Shadow it is
    /// their own role, untouched. In a company switched to Enforced it is Access.AccessProjection's answer: the
    /// Company administrator reads as Admin; anybody else holding the permission as Manager, and the
    /// manager-shaped lists then narrow to where they hold it (<see cref="ManagedLocationIdsAsync"/> — the
    /// report's permission joins the request's, for exactly that); nobody else as Employee (own records).
    /// A bridge, so the reports and boards that grew up around the two roles keep their tested paths.
    ///
    /// A report called inside another — the payroll prices a summary — is held under the role the outer one
    /// got (IAuthzObserver.RoleCeiling): a timekeeper refused the payroll is plain staff for it, and the summary
    /// it builds on must not hand them back their branch because they hold «attendance.view» there.
    /// </summary>
    public static async Task<EmployeeRole> EffectiveRoleAsync(
        AppDbContext db, Guid callerId, EmployeeRole role, string permission, CancellationToken ct)
    {
        if (db.AuthzObserver is not { } observer || await observer.ModeAsync(db, ct) != AuthzMode.Enforced)
            return role;
        observer.AddRequestPermission(permission);
        var projected = await observer.ProjectRoleAsync(db, callerId, role, permission, ct);
        if (observer.RoleCeiling is { } ceiling && ceiling < projected)
            projected = ceiling;
        observer.RoleCeiling = projected;
        return projected;
    }

    /// <summary>Where <paramref name="permission"/> and every right of this request reach — what a report narrows
    /// itself to in Enforced (IAuthzObserver.RequestScopeAsync); today's scope elsewhere.</summary>
    public static async Task<Access.ScopeAnswer> RequestScopeAsync(
        AppDbContext db, Guid callerId, EmployeeRole role, string permission, CancellationToken ct)
    {
        if (db.AuthzObserver is { } observer)
            return await observer.RequestScopeAsync(db, callerId, role, permission, ct);
        return await ScopeAsync(db, callerId, role, permission, ct);
    }

    // Today's rules, unchanged. In Legacy and Shadow the public methods return exactly what these return (the
    // model only watches — docs/strukturlar-ve-selahiyyetler.md §4.3); in Enforced the observer's answer.
    private static Task<List<Guid>> LegacyManagedAsync(AppDbContext db, Guid managerId, CancellationToken ct)
        => db.ManagedLocations
            .Where(m => m.EmployeeId == managerId)
            .Select(m => m.LocationId)
            .ToListAsync(ct);

    /// <summary>
    /// Whether <paramref name="requesterId"/> may SEE <paramref name="targetEmployeeId"/>'s data
    /// (attendance drill-down, check-in selfies, missed-checkout detail).
    ///
    /// Seeing is wider than managing, and the two came apart on purpose. A manager's boards used to
    /// stop at Role==Employee, which meant a site with two managers showed each of them a headcount
    /// that was short by the other — a branch of 97 read as 95, and no screen explained the gap. That
    /// is not a safeguard, it is a wrong number: they work the same site and see each other every
    /// morning.
    ///
    /// What the Role==Employee rule was actually protecting is ACTING on somebody — resetting a PIN,
    /// editing an account, closing their day — because a manager who could act on a same-branch ADMIN
    /// could take the company (the P0 of 2026-08-08). That rule is untouched and lives in
    /// <see cref="CanManageEmployeeAsync"/>. This one only decides who is visible, and the answer is
    /// everyone at the branches they manage.
    /// </summary>
    /// <param name="permission">What the records are, where it is not the request's own [Permission] (an
    /// endpoint the employee app shares): «attendance.photos.view» for a selfie, «attendance.view» for a day.
    /// Read only in Enforced — today's admins and managers hold both wherever they see people.</param>
    public static async Task<bool> CanAccessEmployeeAsync(
        AppDbContext db, Guid requesterId, EmployeeRole role, Guid targetEmployeeId, CancellationToken ct,
        string? permission = null)
    {
        var allowed = await LegacyCanAccessAsync(db, requesterId, role, targetEmployeeId, ct);
        if (db.AuthzObserver is { } observer)
            // One's own records stay one's own in every mode — the model's rule is about other people.
            return requesterId == targetEmployeeId
                || await observer.DecideScopeAsync(db, Access.ShadowRules.CanAccess, requesterId, role, targetEmployeeId, allowed, permission, ct);
        return allowed;
    }

    private static async Task<bool> LegacyCanAccessAsync(
        AppDbContext db, Guid requesterId, EmployeeRole role, Guid targetEmployeeId, CancellationToken ct)
    {
        // Everyone can always see their own records.
        if (requesterId == targetEmployeeId)
            return true;

        if (role == EmployeeRole.Admin)
            return true;
        if (role != EmployeeRole.Manager)
            return false;

        // Anyone at a branch this manager oversees, whatever their role. Note this includes an ADMIN
        // who clocks in at that branch: a manager can see that they arrived, and their check-in
        // selfie, exactly as for anyone else standing at the same gate. They still cannot touch the
        // account — see CanManageEmployeeAsync, which is what reset-pin, edit and delete ask.
        var location = await db.Employees
            .Where(e => e.Id == targetEmployeeId)
            .Select(e => (Guid?)e.LocationId)
            .FirstOrDefaultAsync(ct);
        if (location is not Guid branch)
            return false;

        var managed = await LegacyManagedAsync(db, requesterId, ct);
        return managed.Contains(branch);
    }

    /// <summary>
    /// Whether <paramref name="requesterId"/> may ROSTER <paramref name="targetEmployeeId"/> — say
    /// which shift one of their days is judged against (a cover night, a swap).
    ///
    /// Deliberately the SEEING scope, not the managing one, and that is the whole point of it being
    /// its own question. What <see cref="CanManageEmployeeAsync"/>'s Role==Employee rule protects is
    /// the ACCOUNT — the PIN, the role, the phone number somebody logs in with — because a manager
    /// who could act on a same-branch admin could take the company (the P0 of 2026-08-08). A cover
    /// day touches none of that: it changes which hours a shift is measured by, nothing else, and it
    /// is stamped with the name of whoever recorded it.
    ///
    /// Refusing it made the roster wrong rather than making anything safer. A site with two managers
    /// covering each other's nights could not record that fact about the other, so those nights were
    /// scored against the wrong shift — the exact failure the override exists to prevent, reintroduced
    /// by a rule written for a different danger. The person who knows a colleague covered last night
    /// is the manager standing beside them.
    ///
    /// A manager may roster themselves — one who worked the night IS the person who knows it. An
    /// ordinary employee may roster NOBODY, themselves least of all: the shift a day is measured by
    /// decides whether that day was late, short, or a day off at all, so letting a worker pick it
    /// would let them retire their own absence. That is the one line where this cannot borrow
    /// <see cref="CanAccessEmployeeAsync"/>, which allows self by design because reading your own
    /// record is not the same act as rewriting how it is judged.
    /// </summary>
    public static async Task<bool> CanScheduleEmployeeAsync(
        AppDbContext db, Guid requesterId, EmployeeRole role, Guid targetEmployeeId, CancellationToken ct)
    {
        var allowed = await LegacyCanScheduleAsync(db, requesterId, role, targetEmployeeId, ct);
        if (db.AuthzObserver is { } observer)
            return await observer.DecideScopeAsync(db, Access.ShadowRules.CanSchedule, requesterId, role, targetEmployeeId, allowed, ct);
        return allowed;
    }

    private static async Task<bool> LegacyCanScheduleAsync(
        AppDbContext db, Guid requesterId, EmployeeRole role, Guid targetEmployeeId, CancellationToken ct)
    {
        if (role == EmployeeRole.Admin)
            return true;
        if (role != EmployeeRole.Manager)
            return false;

        // Anyone standing at a branch this manager runs, whatever their role — including themselves.
        return await LegacyCanAccessAsync(db, requesterId, role, targetEmployeeId, ct);
    }

    /// <summary>
    /// Whether <paramref name="requesterId"/> may MANAGE <paramref name="targetEmployeeId"/> — act on
    /// their account or records (assign/cancel field visits, pull their selfies, close their days).
    /// Stricter than <see cref="CanAccessEmployeeAsync"/> on two points, both learned the hard way:
    /// a Manager's reach stops at <c>Role == Employee</c> (an Admin or a fellow Manager also carries a
    /// LocationId — where they clock in — so branch membership alone would make the people ABOVE the
    /// manager valid targets), and self is NOT automatically included (managing means acting on another
    /// account; a manager's own visits go through the worker flow). Admin manages anyone in the tenant;
    /// tenant isolation itself comes from the global query filter on Employees. Fail-closed.
    ///
    /// One widening since, asked for by the owner (2026-09-18): a fellow MANAGER at one of the caller's
    /// OWN branches — the colleague standing next to them, whose missed check-out, day off or «qayıb»
    /// used to wait for the one admin. The branch rule still carries it: a manager at a branch the
    /// caller does not run stays out of reach and out of sight (a first cut of this reached every
    /// manager in the company, and the owner said no the same hour). What 2026-08-08 closed stays
    /// closed: never an ADMIN, never the caller themself, and a role added later falls through to «no».
    /// </summary>
    public static async Task<bool> CanManageEmployeeAsync(
        AppDbContext db, Guid requesterId, EmployeeRole role, Guid targetEmployeeId, CancellationToken ct)
    {
        var allowed = await LegacyCanManageAsync(db, requesterId, role, targetEmployeeId, ct);
        if (db.AuthzObserver is { } observer)
            return await observer.DecideScopeAsync(db, Access.ShadowRules.CanManage, requesterId, role, targetEmployeeId, allowed, ct);
        return allowed;
    }

    /// <summary>
    /// Whether <paramref name="requesterId"/> may CORRECT <paramref name="targetEmployeeId"/>'s attendance —
    /// edit, create, close, clear or delete a day, mark an absence, decide a missed check-out. Today's manage
    /// rule, with one thing the owner ruled out on 2026-10-15: one's OWN attendance, whoever one is — an
    /// admin included. The day a person is paid for is never fixed by that person.
    /// </summary>
    public static async Task<bool> CanCorrectAttendanceAsync(
        AppDbContext db, Guid requesterId, EmployeeRole role, Guid targetEmployeeId, CancellationToken ct)
    {
        var allowed = requesterId != targetEmployeeId
                      && await LegacyCanManageAsync(db, requesterId, role, targetEmployeeId, ct);
        if (db.AuthzObserver is { } observer)
            return await observer.DecideScopeAsync(db, Access.ShadowRules.CorrectAttendance, requesterId, role, targetEmployeeId, allowed, ct);
        return allowed;
    }

    /// <summary>
    /// In a company switched to Enforced: may the caller hand this person a new PIN or login number, by the
    /// model's strict account rule (ShadowRules.CredentialTarget — only from above, never a colleague of the same
    /// rank, never an operator, never oneself). True anywhere else, where the call site's own checks decide
    /// exactly as before. For a path that mints a credential behind a check written for RECORDS — the PIN-reset
    /// queue asks <see cref="CanManageEmployeeAsync"/>, which in Enforced would let one timekeeper answer
    /// another's request and read their new PIN.
    /// </summary>
    public static async Task<bool> EnforcedCredentialTargetAsync(
        AppDbContext db, Guid requesterId, EmployeeRole role, Guid targetEmployeeId, CancellationToken ct)
    {
        if (db.AuthzObserver is not { } observer || await observer.ModeAsync(db, ct) != AuthzMode.Enforced)
            return true;
        return await observer.DecideScopeAsync(db, Access.ShadowRules.CredentialTarget, requesterId, role, targetEmployeeId,
            legacy: false, ct);
    }

    private static async Task<bool> LegacyCanManageAsync(
        AppDbContext db, Guid requesterId, EmployeeRole role, Guid targetEmployeeId, CancellationToken ct)
    {
        if (role == EmployeeRole.Admin)
            return true;
        if (role != EmployeeRole.Manager)
            return false;

        var target = await db.Employees
            .Where(e => e.Id == targetEmployeeId)
            .Select(e => new { e.LocationId, e.Role })
            .FirstOrDefaultAsync(ct);
        if (target is null)
            return false;
        // Plain staff, or a fellow manager — never an admin, never oneself. Either way, only at a
        // branch the caller manages (below).
        if (target.Role == EmployeeRole.Manager ? targetEmployeeId == requesterId : target.Role != EmployeeRole.Employee)
            return false;

        var managed = await LegacyManagedAsync(db, requesterId, ct);
        return managed.Contains(target.LocationId);
    }
}
