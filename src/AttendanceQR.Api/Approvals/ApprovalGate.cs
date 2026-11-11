using System.Text.Json;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Approvals;

/// <summary>
/// The line every direct change crosses at a branch that may be in an approval area. Today this means adding a
/// person. It runs after the endpoint's own checks (role, branch, rights), and it can only ever take away.
///
/// <list type="bullet">
/// <item>Approvals switched off, or the branch outside every area: <see cref="Verdict.Outside"/>. Nothing changes,
/// and nothing is recorded. This is every company today.</item>
/// <item>Inside an area, for somebody who decides there (the operator, the Company administrator):
/// <see cref="Verdict.Direct"/>. The change goes ahead and is recorded as a Direct row with
/// <see cref="RecordDirect"/>, in the same save as the change.</item>
/// <item>Inside an area, for anybody else: refused with 403 «ApprovalRequired». This holds in every AuthzMode,
/// because it reads the permission model rather than the token's role. A timekeeper who is still a Manager
/// by today's role is refused here all the same. <c>canRequest</c> tells the screen whether to offer
/// «Müraciət göndər» instead.</item>
/// </list>
/// </summary>
public static class ApprovalGate
{
    public enum Outcome { Outside, Direct, Refused }

    public sealed record Verdict(Outcome Outcome, Guid? StructureId = null, bool CanRequest = false)
    {
        public static readonly Verdict Outside = new(Outcome.Outside);

        /// <summary>The 403 body for a refusal. Its shape is shared by every direct path.</summary>
        public object RefusalBody => new { error = "ApprovalRequired", canRequest = CanRequest };
    }

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<Verdict> DirectChangeAsync(
        AppDbContext db, ApprovalOptions? options, Guid callerId, Guid locationId, CancellationToken ct)
    {
        if (options?.Enabled != true)
            return Verdict.Outside;
        // The common case, every company without an area, costs one indexed EXISTS and nothing else.
        if (!await db.ApprovalAreas.AnyAsync(a => a.DeactivatedAtUtc == null, ct))
            return Verdict.Outside;
        var scope = await ApprovalScope.LoadAsync(db, DateTime.UtcNow, ct);
        if (!scope.InArea(locationId))
            return Verdict.Outside;
        var structure = scope.Branch(locationId)?.StructureId;
        return scope.MayActDirectly(callerId, locationId)
            ? new Verdict(Outcome.Direct, structure)
            : new Verdict(Outcome.Refused, structure, scope.MayRequest(callerId, locationId));
    }

    /// <summary>
    /// Records a direct new person in an approval area: a Direct, Approved row and its history line. It is added
    /// to the context and nothing is saved, so the record and the account it describes are written by one
    /// SaveChanges or not at all.
    /// </summary>
    public static void RecordDirect(
        AppDbContext db, Verdict verdict, Guid actorId, Employee employee, DateTime nowUtc)
    {
        if (verdict.Outcome != Outcome.Direct)
            return;
        var request = new ChangeRequest
        {
            Type = ChangeRequestType.EmployeeCreate,
            Status = ChangeRequestStatus.Approved,
            Direct = true,
            LocationId = employee.LocationId,
            StructureIdAtCreation = verdict.StructureId,
            EmployeeId = employee.Id,
            Payload = JsonSerializer.Serialize(EmployeeCreatePayload.From(employee), Json),
            RequestedByEmployeeId = actorId,
            CreatedAtUtc = nowUtc,
            DecidedByEmployeeId = actorId,
            DecidedAtUtc = nowUtc,
            AppliedAtUtc = nowUtc,
        };
        db.ChangeRequests.Add(request);
        db.ChangeRequestEvents.Add(new ChangeRequestEvent
        {
            ChangeRequestId = request.Id,
            Kind = ChangeRequestEventKind.Direct,
            ActorEmployeeId = actorId,
            AtUtc = nowUtc,
            Data = JsonSerializer.Serialize(new { employeeId = employee.Id }, Json),
        });
    }
}

/// <summary>
/// What a «Yeni işçi» request asks for, as it is stored in ChangeRequest.Payload. It holds the form's fields and
/// nothing else. The person's check-in exceptions (field check-in, their own QR-free or GPS-wall setting, the
/// photo exemption) are not part of it. They are rights of their own («field_checkin.grant»,
/// «checkin_rules.override»), and a request must not carry them past an approver who does not hold them. A new
/// person starts with the branch's rules, and an exception is a separate decision.
/// </summary>
public sealed record EmployeeCreatePayload(
    string FirstName,
    string LastName,
    string? FatherName,
    string? PhoneNumber,
    string? Email,
    string? Position,
    DateOnly? BirthDate,
    Guid LocationId,
    Guid? ScheduleId)
{
    public string FullName => $"{FirstName} {LastName}";

    public static EmployeeCreatePayload From(Employee e) => new(
        e.FirstName ?? e.FullName, e.LastName ?? string.Empty, e.FatherName, e.PhoneNumber, e.Email, e.Position,
        e.BirthDate, e.LocationId, e.ScheduleId);

    /// <summary>The manager desk's own request for the same person, so the approval adds them through the very
    /// rules the desk uses (NewEmployee). The exceptions stay at their safe defaults.</summary>
    public Contracts.ManagerEmployeeRequest ToDeskRequest() => new(
        FullName: FullName,
        Email: Email,
        PhoneNumber: PhoneNumber,
        FatherName: FatherName,
        Position: Position,
        LocationId: LocationId,
        BirthDate: BirthDate,
        ScheduleId: ScheduleId,
        FirstName: FirstName,
        LastName: LastName);
}
