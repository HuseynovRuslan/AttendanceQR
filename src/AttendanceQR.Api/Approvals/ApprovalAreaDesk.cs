using AttendanceQR.Domain.Access;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services.Access;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Approvals;

public sealed record ApprovalAreaBody(AccessScopeType Scope, Guid? StructureId, Guid? LocationId);

public sealed record SubstitutionBody(
    Guid StructureId, Guid SubstituteEmployeeId, DateTime? StartsAtUtc, DateTime EndsAtUtc, string? Note);

/// <summary>
/// Switching the approval flow on and off, area by area, and an Operator's time-limited stand-in for another
/// structure. Both are the Company administrator's alone. Each change is one audit line.
///
/// Switching on is safe by construction:
/// <list type="bullet">
/// <item>it reaches a structure or one branch of a structure, never the company and never a branch with no
/// structure;</item>
/// <item>it is refused while nobody active would decide there, because requests nobody can answer would only
/// pile up;</item>
/// <item>pressing it twice is one area.</item>
/// </list>
/// Switching off leaves the waiting requests where they are. Their approvers still decide them, and the person
/// who asked can still withdraw. Only new changes are made directly again.
/// </summary>
public sealed class ApprovalAreaDesk
{
    /// <summary>The longest stand-in. A leave longer than a month is a new operator, not a stand-in.</summary>
    public static readonly TimeSpan MaxStandIn = TimeSpan.FromDays(31);

    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;

    public ApprovalAreaDesk(AppDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public async Task<DeskResult> ListAreasAsync(Guid callerId, CancellationToken ct)
    {
        var access = (await AccessGraph.LoadAsync(_db, ct)).For(callerId);
        if (!AccessProjection.IsCompanyAdmin(access)
            && !access.Has(AccessPermissions.RequestsView) && !access.Has(AccessPermissions.RequestsCreate)
            && !access.Has(AccessPermissions.RequestsDecide))
            return new DeskResult(403, new { error = "NotAllowed" });
        var areas = await _db.ApprovalAreas.AsNoTracking()
            .Where(a => a.DeactivatedAtUtc == null)
            .OrderBy(a => a.ActivatedAtUtc)
            .ToListAsync(ct);
        var structures = await _db.Structures.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var branches = await _db.Locations.AsNoTracking().ToDictionaryAsync(l => l.Id, l => l.Name, ct);
        return new DeskResult(200, new
        {
            items = areas.Select(a => new
            {
                id = a.Id,
                scope = a.Scope,
                structureId = a.StructureId,
                structureName = a.StructureId is Guid s ? structures.GetValueOrDefault(s) : null,
                locationId = a.LocationId,
                locationName = a.LocationId is Guid l ? branches.GetValueOrDefault(l) : null,
                activatedAtUtc = a.ActivatedAtUtc,
            }),
        });
    }

    public async Task<DeskResult> ActivateAsync(Guid callerId, ApprovalAreaBody body, string? ip, CancellationToken ct)
    {
        var now = Now;
        var scope = await ApprovalScope.LoadAsync(_db, now, ct);
        if (!AccessProjection.IsCompanyAdmin(scope.AccessOf(callerId)))
            return Forbidden();

        string label;
        Guid? structureId = null, locationId = null;
        switch (body.Scope)
        {
            case AccessScopeType.Structure when body.StructureId is Guid s && body.LocationId is null:
                var structure = await _db.Structures.AsNoTracking().FirstOrDefaultAsync(x => x.Id == s, ct);
                if (structure is null)
                    return new DeskResult(404, new { error = "StructureNotFound" });
                if (structure.ArchivedAtUtc is not null)
                    return new DeskResult(409, new { error = "StructureArchived" });
                structureId = s;
                label = $"struktur «{structure.Name}»";
                break;
            case AccessScopeType.Branch when body.LocationId is Guid l && body.StructureId is null:
                if (scope.Branch(l) is not { } branch)
                    return new DeskResult(404, new { error = "LocationNotFound" });
                // The owner's rule 1: a branch with no structure stays on today's flow, and nobody else is asked.
                if (branch.StructureId is null)
                    return new DeskResult(409, new { error = "BranchHasNoStructure" });
                locationId = l;
                label = $"filial «{branch.Name}»";
                break;
            default:
                return new DeskResult(400, new { error = "InvalidArea" });
        }

        var live = await _db.ApprovalAreas.FirstOrDefaultAsync(a => a.DeactivatedAtUtc == null
            && a.Scope == body.Scope && a.StructureId == structureId && a.LocationId == locationId, ct);
        if (live is not null)
            return new DeskResult(200, new { id = live.Id, replayed = true });

        var candidates = scope.ApproverCandidates(body.Scope, structureId, locationId);
        var anyActive = candidates.Count > 0
                        && await _db.Employees.AnyAsync(e => candidates.Contains(e.Id) && e.IsActive, ct);
        if (!anyActive)
            return new DeskResult(409, new { error = "NoApproverInArea" });

        var area = new ApprovalArea
        {
            Scope = body.Scope,
            StructureId = structureId,
            LocationId = locationId,
            ActivatedAtUtc = now,
            ActivatedByEmployeeId = callerId,
        };
        _db.ApprovalAreas.Add(area);
        Audit(callerId, AuditEventType.ApprovalAreaChanged, $"Təsdiq sistemi açıldı: {label}", ip, now);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ChangeRequestDesk.UniqueViolation(ex) is not null)
        {
            // Two admins pressing at once: the index lets one area through. The other press sees it.
            _db.ChangeTracker.Clear();
            var raced = await _db.ApprovalAreas.AsNoTracking().FirstAsync(a => a.DeactivatedAtUtc == null
                && a.Scope == body.Scope && a.StructureId == structureId && a.LocationId == locationId, ct);
            return new DeskResult(200, new { id = raced.Id, replayed = true });
        }
        return new DeskResult(201, new { id = area.Id, replayed = false });
    }

    public async Task<DeskResult> DeactivateAsync(Guid callerId, Guid id, string? ip, CancellationToken ct)
    {
        var now = Now;
        var access = (await AccessGraph.LoadAsync(_db, ct)).For(callerId);
        if (!AccessProjection.IsCompanyAdmin(access))
            return Forbidden();
        var area = await _db.ApprovalAreas.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (area is null)
            return new DeskResult(404, new { error = "AreaNotFound" });
        if (area.DeactivatedAtUtc is not null)
            return new DeskResult(200, new { id, replayed = true });
        area.DeactivatedAtUtc = now;
        area.DeactivatedByEmployeeId = callerId;
        var name = area.StructureId is Guid s
            ? "struktur «" + await _db.Structures.Where(x => x.Id == s).Select(x => x.Name).FirstOrDefaultAsync(ct) + "»"
            : "filial «" + await _db.Locations.Where(x => x.Id == area.LocationId).Select(x => x.Name).FirstOrDefaultAsync(ct) + "»";
        Audit(callerId, AuditEventType.ApprovalAreaChanged, $"Təsdiq sistemi bağlandı: {name}", ip, now);
        await _db.SaveChangesAsync(ct);
        return new DeskResult(200, new { id, replayed = false });
    }

    // --- stand-ins ---------------------------------------------------------------------------------------

    public async Task<DeskResult> ListSubstitutionsAsync(Guid callerId, CancellationToken ct)
    {
        var access = (await AccessGraph.LoadAsync(_db, ct)).For(callerId);
        if (!AccessProjection.IsCompanyAdmin(access))
            return Forbidden();
        var now = Now;
        var rows = await _db.ApproverSubstitutions.AsNoTracking()
            .Where(s => s.RevokedAtUtc == null && s.EndsAtUtc > now)
            .OrderBy(s => s.StartsAtUtc)
            .ToListAsync(ct);
        var ids = rows.Select(r => r.SubstituteEmployeeId).Distinct().ToList();
        var people = await _db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        var structures = await _db.Structures.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        return new DeskResult(200, new
        {
            items = rows.Select(r => new
            {
                id = r.Id,
                structureId = r.StructureId,
                structureName = structures.GetValueOrDefault(r.StructureId),
                substituteEmployeeId = r.SubstituteEmployeeId,
                substituteName = people.GetValueOrDefault(r.SubstituteEmployeeId),
                startsAtUtc = r.StartsAtUtc,
                endsAtUtc = r.EndsAtUtc,
                active = r.StartsAtUtc <= now,
                note = r.Note,
            }),
        });
    }

    public async Task<DeskResult> CreateSubstitutionAsync(Guid callerId, SubstitutionBody body, string? ip, CancellationToken ct)
    {
        var now = Now;
        var scope = await ApprovalScope.LoadAsync(_db, now, ct);
        if (!AccessProjection.IsCompanyAdmin(scope.AccessOf(callerId)))
            return Forbidden();
        if ((body.Note?.Trim().Length ?? 0) > ChangeRequestDesk.MaxNote)
            return new DeskResult(400, new { error = "NoteTooLong" });

        var structure = await _db.Structures.AsNoTracking().FirstOrDefaultAsync(s => s.Id == body.StructureId, ct);
        if (structure is null)
            return new DeskResult(404, new { error = "StructureNotFound" });
        if (structure.ArchivedAtUtc is not null)
            return new DeskResult(409, new { error = "StructureArchived" });

        var substitute = await _db.Employees.AsNoTracking()
            .Where(e => e.Id == body.SubstituteEmployeeId)
            .Select(e => new { e.IsActive, e.FullName })
            .FirstOrDefaultAsync(ct);
        if (substitute is null || !substitute.IsActive)
            return new DeskResult(404, new { error = "EmployeeNotFound" });
        // The owner's rule 6: the stand-in is somebody who is already an Operator. An observer, a timekeeper or a
        // Company administrator who decides nowhere is never made an approver this way.
        if (!scope.IsOperator(body.SubstituteEmployeeId))
            return new DeskResult(409, new { error = "SubstituteMustBeOperator" });
        // Already deciding this structure's requests: a stand-in would add nothing.
        if (scope.DecidesStructure(body.SubstituteEmployeeId, body.StructureId))
            return new DeskResult(409, new { error = "AlreadyApproverOfStructure" });

        var starts = DateTime.SpecifyKind(body.StartsAtUtc ?? now, DateTimeKind.Utc);
        var ends = DateTime.SpecifyKind(body.EndsAtUtc, DateTimeKind.Utc);
        if (ends <= starts || ends <= now)
            return new DeskResult(400, new { error = "InvalidPeriod" });
        if (ends - starts > MaxStandIn)
            return new DeskResult(400, new { error = "SubstitutionTooLong" });
        // One stand-in per person and structure at a time. To lengthen one, end it and give a new one, so the
        // history says what happened.
        if (await _db.ApproverSubstitutions.AnyAsync(s => s.RevokedAtUtc == null && s.StructureId == body.StructureId
                && s.SubstituteEmployeeId == body.SubstituteEmployeeId && s.StartsAtUtc < ends && s.EndsAtUtc > starts, ct))
            return new DeskResult(409, new { error = "SubstitutionOverlaps" });

        var row = new ApproverSubstitution
        {
            StructureId = body.StructureId,
            SubstituteEmployeeId = body.SubstituteEmployeeId,
            StartsAtUtc = starts,
            EndsAtUtc = ends,
            Note = body.Note?.Trim() is { Length: > 0 } n ? n : null,
            CreatedByEmployeeId = callerId,
            CreatedAtUtc = now,
        };
        _db.ApproverSubstitutions.Add(row);
        Audit(callerId, AuditEventType.ApproverSubstitutionChanged,
            $"Əvəzetmə: {substitute.FullName}, struktur «{structure.Name}», {starts:yyyy-MM-dd HH:mm} – {ends:yyyy-MM-dd HH:mm} UTC",
            ip, now);
        await _db.SaveChangesAsync(ct);
        return new DeskResult(201, new { id = row.Id });
    }

    public async Task<DeskResult> RevokeSubstitutionAsync(Guid callerId, Guid id, string? ip, CancellationToken ct)
    {
        var now = Now;
        var access = (await AccessGraph.LoadAsync(_db, ct)).For(callerId);
        if (!AccessProjection.IsCompanyAdmin(access))
            return Forbidden();
        var row = await _db.ApproverSubstitutions.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (row is null)
            return new DeskResult(404, new { error = "SubstitutionNotFound" });
        if (row.RevokedAtUtc is not null)
            return new DeskResult(200, new { id, replayed = true });
        row.RevokedAtUtc = now;
        row.RevokedByEmployeeId = callerId;
        Audit(callerId, AuditEventType.ApproverSubstitutionChanged, "Əvəzetmə vaxtından əvvəl bitirildi", ip, now);
        await _db.SaveChangesAsync(ct);
        return new DeskResult(200, new { id, replayed = false });
    }

    private void Audit(Guid actorId, AuditEventType type, string reason, string? ip, DateTime now) =>
        _db.AuditLogs.Add(new AuditLog
        {
            EmployeeId = actorId,
            EventType = type,
            Reason = reason,
            IpAddress = ip,
            CreatedAtUtc = now,
        });

    private static DeskResult Forbidden() => new(403, new { error = "CompanyAdminOnly" });
}
