using AttendanceQR.Domain.Access;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services.Access;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Infrastructure.Services;

public enum SelfCorrectionAnswer
{
    /// <summary>Not theirs to correct: somebody else can, or they could not correct anybody's day anyway.</summary>
    Refused,
    /// <summary>Nobody else could: it goes ahead, in the open.</summary>
    Apply,
    /// <summary>Nobody else could correct it, and the company asks for a second pair of eyes: it waits.</summary>
    AwaitApproval,
}

/// <param name="Correctors">On a refusal because somebody else can: their names, so the screen says who to ask.</param>
public sealed record SelfCorrectionVerdict(SelfCorrectionAnswer Answer, IReadOnlyList<string> Correctors)
{
    public bool Refused => Answer == SelfCorrectionAnswer.Refused;
}

/// <summary>
/// One's own attendance (docs/strukturlar-ve-selahiyyetler.md §18). The owner's rule of 2026-10-15 stands —
/// nobody corrects the day they are paid for — with one opening, the owner's of 2026-10-16: where the company
/// has NOBODY else who could correct that day (one admin, no second person with «attendance.correct» over it),
/// the person may, in the open. No company has to keep a second admin for this.
///
/// In the open means: a reason (3–500 characters); never a deletion; the old times kept for good in
/// <see cref="AttendanceSelfCorrection"/> while the day is recomputed with the new; an audit event of its own;
/// «öz düzəlişi» on the board, the profile, the tabel and the payroll; and, where the company asks, a second
/// approval before anything is applied.
///
/// «Could correct» is the company's own rule — today's in Legacy and Shadow, the permission model's in Enforced —
/// so the opening closes by itself the day a second admin, or a manager over the admin's branch, appears.
/// </summary>
public static class SelfCorrection
{
    public const string Update = "Update";
    public const string Create = "Create";
    public const string ClearCheckout = "ClearCheckout";
    public const string CloseOpen = "CloseOpen";
    public const string MissedCheckout = "MissedCheckout";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";

    /// <summary>The answer for a correction <paramref name="employeeId"/> wants to make to their own day.</summary>
    public static async Task<SelfCorrectionVerdict> AskAsync(
        AppDbContext db, Guid employeeId, EmployeeRole role, bool impersonating, CancellationToken ct = default)
    {
        // A support session is not the person: it never corrects «their own» day on their behalf.
        if (impersonating || !await MayCorrectOwnAsync(db, employeeId, role, ct))
            return new(SelfCorrectionAnswer.Refused, []);
        var others = await OtherCorrectorsAsync(db, employeeId, ct);
        if (others.Count > 0)
            return new(SelfCorrectionAnswer.Refused, others);
        var needsApproval = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == db.CurrentTenantId).Select(t => t.SelfCorrectionNeedsApproval).FirstOrDefaultAsync(ct);
        return new(needsApproval ? SelfCorrectionAnswer.AwaitApproval : SelfCorrectionAnswer.Apply, []);
    }

    /// <summary>
    /// Whether they could correct days at all from where they stand over their own: today an admin (a manager
    /// reaches plain staff and colleagues, never themselves); in Enforced «attendance.correct» company-wide —
    /// the model's rule for one's own records.
    /// </summary>
    public static async Task<bool> MayCorrectOwnAsync(AppDbContext db, Guid employeeId, EmployeeRole role, CancellationToken ct = default)
    {
        if (db.AuthzObserver is { } observer && await observer.ModeAsync(db, ct) == AuthzMode.Enforced)
            return (await observer.GraphAsync(db, ct)).For(employeeId).HasAtCompany(AccessPermissions.AttendanceCorrect);
        return role == EmployeeRole.Admin;
    }

    /// <summary>
    /// The active people, other than the person, who could correct their day under the company's own rule —
    /// today's in Legacy and Shadow (every other admin; a manager of their branch when they are plain staff or a
    /// fellow manager there), the model's in Enforced. Never a platform operator: they are not the company's staff.
    /// </summary>
    public static async Task<List<string>> OtherCorrectorsAsync(AppDbContext db, Guid employeeId, CancellationToken ct = default)
    {
        var target = await db.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId).Select(e => new { e.LocationId, e.Role }).FirstOrDefaultAsync(ct);
        if (target is null)
            return [];
        var operators = db.AuthzObserver?.OperatorIds ?? new HashSet<Guid>();

        if (db.AuthzObserver is { } observer && await observer.ModeAsync(db, ct) == AuthzMode.Enforced)
        {
            var graph = await observer.GraphAsync(db, ct);
            var holders = graph.People.Where(id => id != employeeId && !operators.Contains(id)).ToList();
            var people = await db.Employees.AsNoTracking()
                .Where(e => e.IsActive && holders.Contains(e.Id))
                .Select(e => new { e.Id, e.FullName, e.LocationId })
                .ToListAsync(ct);
            var targetAccess = graph.For(employeeId);
            return people
                .Where(p => ShadowRules.Decide(ShadowRules.CorrectAttendance, graph.For(p.Id), employeeId, target.LocationId,
                    targetAccess, operators.Contains(employeeId), p.LocationId))
                .Select(p => p.FullName).Order().ToList();
        }

        var admins = await db.Employees.AsNoTracking()
            .Where(e => e.IsActive && e.Id != employeeId && e.Role == EmployeeRole.Admin)
            .Select(e => new { e.Id, e.FullName })
            .ToListAsync(ct);
        var managers = target.Role is EmployeeRole.Employee or EmployeeRole.Manager
            ? await db.Employees.AsNoTracking()
                .Where(e => e.IsActive && e.Id != employeeId && e.Role == EmployeeRole.Manager
                            && db.ManagedLocations.Any(m => m.EmployeeId == e.Id && m.LocationId == target.LocationId))
                .Select(e => new { e.Id, e.FullName })
                .ToListAsync(ct)
            : [];
        return admins.Concat(managers).Where(p => !operators.Contains(p.Id)).Select(p => p.FullName).Distinct().Order().ToList();
    }

    /// <summary>
    /// Who may answer a self-correction that waits (the company asked for a second approval): the active people,
    /// other than the person, who hold «attendance.self_correction.approve» and «attendance.view» over their
    /// branch in Enforced — another admin today. The company's owner given an observer role and the approval right
    /// is the case this is for: somebody who confirms, without being somebody who corrects.
    /// </summary>
    public static async Task<List<Guid>> ApproversAsync(AppDbContext db, Guid employeeId, CancellationToken ct = default)
    {
        var home = await db.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId).Select(e => (Guid?)e.LocationId).FirstOrDefaultAsync(ct);
        if (home is not Guid branch)
            return [];
        var operators = db.AuthzObserver?.OperatorIds ?? new HashSet<Guid>();
        if (db.AuthzObserver is { } observer && await observer.ModeAsync(db, ct) == AuthzMode.Enforced)
        {
            var graph = await observer.GraphAsync(db, ct);
            var holders = graph.People
                .Where(id => id != employeeId && !operators.Contains(id)
                             && graph.For(id).Covers(AccessPermissions.AttendanceSelfCorrectionApprove, branch)
                             && graph.For(id).Covers(AccessPermissions.AttendanceView, branch))
                .ToList();
            return await db.Employees.AsNoTracking().Where(e => e.IsActive && holders.Contains(e.Id)).Select(e => e.Id).ToListAsync(ct);
        }
        var admins = await db.Employees.AsNoTracking()
            .Where(e => e.IsActive && e.Id != employeeId && e.Role == EmployeeRole.Admin)
            .Select(e => e.Id).ToListAsync(ct);
        return admins.Where(id => !operators.Contains(id)).ToList();
    }

    /// <summary>
    /// Writes the ledger row and its audit line for a self-correction — applied, or awaiting approval. Not saved
    /// here: it goes in with the correction it describes, so the two cannot come apart.
    /// </summary>
    public static AttendanceSelfCorrection Record(
        AppDbContext db, Guid employeeId, Guid? recordId, DateOnly date, string kind,
        DateTime? checkInBefore, DateTime? checkOutBefore, DateTime? checkInAfter, DateTime? checkOutAfter,
        string reason, bool awaitsApproval, string? ip)
    {
        var row = new AttendanceSelfCorrection
        {
            TenantId = db.CurrentTenantId, EmployeeId = employeeId, ActorEmployeeId = employeeId, RecordId = recordId,
            AttendanceDate = date, Kind = kind, CheckInBeforeUtc = checkInBefore, CheckOutBeforeUtc = checkOutBefore,
            CheckInAfterUtc = checkInAfter, CheckOutAfterUtc = checkOutAfter, Reason = reason, AwaitsApproval = awaitsApproval,
        };
        db.AttendanceSelfCorrections.Add(row);
        db.AuditLogs.Add(new AuditLog
        {
            EmployeeId = employeeId,
            EventType = AuditEventType.AttendanceSelfCorrected,
            Reason = Cap($"{kind}{(awaitsApproval ? " (təsdiq gözləyir)" : "")} {date:yyyy-MM-dd}: "
                         + $"{Times(checkInBefore, checkOutBefore)} → {Times(checkInAfter, checkOutAfter)}. Səbəb: {reason}"),
            IpAddress = ip,
        });
        return row;
    }

    /// <summary>The days that were somebody's own work — applied self-corrections, waiting ones not — for the people
    /// asked about (all, when null) between two dates.</summary>
    public static async Task<HashSet<(Guid EmployeeId, DateOnly Date)>> DaysAsync(
        AppDbContext db, DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? employees, CancellationToken ct = default)
    {
        var query = db.AttendanceSelfCorrections.AsNoTracking()
            .Where(c => c.AttendanceDate >= from && c.AttendanceDate <= to && !c.AwaitsApproval && c.Kind != Rejected);
        if (employees is not null)
            query = query.Where(c => employees.Contains(c.EmployeeId));
        var rows = await query.Select(c => new { c.EmployeeId, c.AttendanceDate }).ToListAsync(ct);
        return rows.Select(r => (r.EmployeeId, r.AttendanceDate)).ToHashSet();
    }

    public static string Times(DateTime? checkIn, DateTime? checkOut) =>
        $"{(checkIn is null ? "—" : checkIn.Value.ToString("HH:mm"))}–{(checkOut is null ? "—" : checkOut.Value.ToString("HH:mm"))} UTC";

    private static string Cap(string text) => text.Length <= 1000 ? text : text[..1000];
}
