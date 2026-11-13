using System.Text.Json;
using AttendanceQR.Domain.Access;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Security;
using AttendanceQR.Infrastructure.Services.Access;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AttendanceQR.Api.Approvals;

/// <summary>An answer of the desk: the HTTP status and the body. The controller only passes it on.</summary>
public sealed record DeskResult(int Status, object Body);

/// <summary>The body of «Yeni işçi» sent as a request. The fields are those of the manager's form, plus «Qeyd».</summary>
public sealed record EmployeeCreateRequestBody(
    string? FirstName,
    string? LastName,
    string? FatherName,
    string? PhoneNumber,
    string? Email,
    string? Position,
    DateOnly? BirthDate,
    Guid LocationId,
    Guid? ScheduleId,
    string? Note);

public sealed record DecisionBody(string? Note);

/// <summary>
/// Requests from creation to decision: who may do what (<see cref="ApprovalScope"/>), the moves of the state
/// machine (<see cref="ChangeRequestRules"/>), and applying an approved change through the code path that makes
/// the same change directly (<see cref="NewEmployee"/>).
///
/// Four promises, each with its test:
/// <list type="bullet">
/// <item>A request is decided once. The move is made against the version that was read, so of two approvers
/// pressing at once one wins and the other is told it is already decided. The database holds the same line
/// with a unique index over decision lines.</item>
/// <item>An approval is the change. Approving and adding the person happen in one transaction. If adding fails
/// (the phone number was given to somebody else meanwhile), nothing is written, the request is still pending,
/// and the reason is in its history.</item>
/// <item>Rights are read at the moment of the act, both when asking and when deciding. A right taken away
/// between the two stops the decision.</item>
/// <item>The same press twice is one request. An Idempotency-Key returns the first answer, and the same person
/// cannot be waiting twice.</item>
/// </list>
/// </summary>
public sealed class ChangeRequestDesk
{
    public const int MaxNote = 500;

    private readonly AppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ApprovalOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<ChangeRequestDesk> _logger;

    public ChangeRequestDesk(
        AppDbContext db, IPasswordHasher hasher, ApprovalOptions options, TimeProvider clock, ILogger<ChangeRequestDesk> logger)
    {
        _db = db;
        _hasher = hasher;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    public bool Enabled => _options.Enabled;

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    // --- create -------------------------------------------------------------------------------------------

    public async Task<DeskResult> CreateEmployeeAsync(
        Guid callerId, EmployeeCreateRequestBody body, string? idempotencyKey, CancellationToken ct)
    {
        var key = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
        if (key is { Length: > 100 })
            return Bad("IdempotencyKeyTooLong");
        if (key is not null && await ReplayCreateAsync(callerId, key, ct) is { } replay)
            return replay;

        if (Validate(body) is { } invalid)
            return Bad(invalid);
        var payload = Normalize(body);
        var now = Now;

        var scope = await ApprovalScope.LoadAsync(_db, now, ct);
        if (scope.Branch(payload.LocationId) is not { } branch)
            return new DeskResult(404, new { error = "LocationNotFound" });
        if (!scope.InArea(branch.Id))
            return new DeskResult(409, new { error = "NotInApprovalArea" });
        if (!scope.MayRequest(callerId, branch.Id))
            return new DeskResult(403, new { error = "RequestNotAllowed" });

        // Job titles come from the catalogue. A request cannot invent one for the approver to discover.
        if (payload.Position is { } position && !await _db.JobPositions.AnyAsync(p => p.Name == position, ct))
            return Bad("PositionNotInCatalogue");
        // The desk's own rules, run now so that a request the approval could never apply is refused while the
        // person who asked is still looking at the form. They run again at the approval.
        var (_, error, conflict) = await NewEmployee.PrepareAsync(_db, _hasher, payload.ToDeskRequest(), ct);
        if (error is not null)
            return new DeskResult(conflict ? 409 : 400, new { error });

        var dedupe = DedupeKeyOf(payload);
        if (await PendingWithAsync(dedupe, ct) is { } waiting)
            return new DeskResult(409, new { error = "DuplicatePendingRequest", number = waiting });

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var request = new ChangeRequest
            {
                Number = (await _db.ChangeRequests.MaxAsync(r => r.Number, ct) ?? 1000) + 1,
                Type = ChangeRequestType.EmployeeCreate,
                Status = ChangeRequestStatus.Pending,
                LocationId = branch.Id,
                StructureIdAtCreation = branch.StructureId,
                Payload = JsonSerializer.Serialize(payload, ApprovalGate.Json),
                Note = body.Note?.Trim() is { Length: > 0 } note ? note : null,
                DedupeKey = dedupe,
                IdempotencyKey = key,
                RequestedByEmployeeId = callerId,
                CreatedAtUtc = now,
            };
            _db.ChangeRequests.Add(request);
            var created = Line(request, ChangeRequestEventKind.Created, callerId, now, request.Note);
            try
            {
                await _db.SaveChangesAsync(ct);
                return new DeskResult(201, Summary(request, branch.Name));
            }
            catch (DbUpdateException ex) when (UniqueViolation(ex) is { } index)
            {
                _db.Entry(request).State = EntityState.Detached;
                _db.Entry(created).State = EntityState.Detached;
                // The same press, sent twice at once, breaks several indexes together (the key, the number, the
                // pending person), and the database names whichever it checked first. The key decides: if the
                // first press is in, this one is its replay, whatever index was named.
                if (key is not null && await ReplayCreateAsync(callerId, key, ct) is { } raced)
                    return raced;
                if (index.Contains("Number", StringComparison.Ordinal))
                    continue;                                   // two requests took the same number: take the next
                if (index.Contains("OnePending", StringComparison.Ordinal))
                    return new DeskResult(409, new { error = "DuplicatePendingRequest", number = await PendingWithAsync(dedupe, ct) });
                throw;
            }
        }
        return new DeskResult(503, new { error = "TryAgain" });
    }

    private async Task<DeskResult?> ReplayCreateAsync(Guid callerId, string key, CancellationToken ct)
    {
        var existing = await _db.ChangeRequests.AsNoTracking().FirstOrDefaultAsync(r => r.IdempotencyKey == key, ct);
        if (existing is null)
            return null;
        // A key is one person's press of one button. Somebody else's key gives them nothing, not even its request.
        if (existing.RequestedByEmployeeId != callerId)
            return new DeskResult(409, new { error = "IdempotencyKeyReused" });
        var name = await _db.Locations.Where(l => l.Id == existing.LocationId).Select(l => l.Name).FirstOrDefaultAsync(ct);
        return new DeskResult(200, Summary(existing, name, replayed: true));
    }

    private static string? Validate(EmployeeCreateRequestBody body)
    {
        if (string.IsNullOrWhiteSpace(body.FirstName) || string.IsNullOrWhiteSpace(body.LastName))
            return "NameRequired";
        if (body.FirstName.Trim().Length > 100 || body.LastName.Trim().Length > 100 || (body.FatherName?.Trim().Length ?? 0) > 100)
            return "NameTooLong";
        if ((body.Position?.Trim().Length ?? 0) > 200)
            return "PositionTooLong";
        if ((body.Email?.Trim().Length ?? 0) > 256)
            return "EmailTooLong";
        if ((body.Note?.Trim().Length ?? 0) > MaxNote)
            return "NoteTooLong";
        if (body.LocationId == Guid.Empty)
            return "LocationRequired";
        return null;
    }

    private static EmployeeCreatePayload Normalize(EmployeeCreateRequestBody body) => new(
        body.FirstName!.Trim(),
        body.LastName!.Trim(),
        string.IsNullOrWhiteSpace(body.FatherName) ? null : body.FatherName.Trim(),
        PhoneNumbers.Normalize(body.PhoneNumber),
        string.IsNullOrWhiteSpace(body.Email) ? null : body.Email.Trim(),
        string.IsNullOrWhiteSpace(body.Position) ? null : body.Position.Trim(),
        body.BirthDate,
        body.LocationId,
        body.ScheduleId);

    /// <summary>The same person is the same phone number (the login), or the same e-mail when there is no phone.</summary>
    internal static string? DedupeKeyOf(EmployeeCreatePayload payload) =>
        payload.PhoneNumber is { } phone ? $"phone:{phone}"
        : payload.Email is { } email ? $"email:{email.ToLowerInvariant()}"
        : null;

    private async Task<int?> PendingWithAsync(string? dedupe, CancellationToken ct) =>
        dedupe is null
            ? null
            : await _db.ChangeRequests.AsNoTracking()
                .Where(r => r.Status == ChangeRequestStatus.Pending && r.Type == ChangeRequestType.EmployeeCreate
                            && r.DedupeKey == dedupe)
                .Select(r => r.Number)
                .FirstOrDefaultAsync(ct);

    // --- decide -------------------------------------------------------------------------------------------

    public async Task<DeskResult> ApproveAsync(Guid callerId, Guid id, string? note, CancellationToken ct)
    {
        if ((note?.Trim().Length ?? 0) > MaxNote)
            return Bad("NoteTooLong");
        var now = Now;
        var (request, scope, refused) = await DecisionTargetAsync(callerId, id, ChangeRequestStatus.Approved, now, ct);
        if (refused is not null)
            return refused;

        var payload = JsonSerializer.Deserialize<EmployeeCreatePayload>(request!.Payload, ApprovalGate.Json)!;
        // Checked before anything is written: the number may have been taken, or the shift removed, since the
        // request was made. The usual refusal leaves the request exactly as it was.
        var (_, error, _) = await NewEmployee.PrepareAsync(_db, _hasher, payload.ToDeskRequest(), ct);
        if (error is not null)
            return await RefuseApplyAsync(request.Id, callerId, error, now, ct);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // 1. The decision, against the version that was read, and nothing else: until this row is ours, nothing
        // is written. A second approver's copy is stale from here on.
        request.Status = ChangeRequestStatus.Approved;
        request.DecidedByEmployeeId = callerId;
        request.DecidedAtUtc = now;
        request.AppliedAtUtc = now;
        request.DecisionNote = note?.Trim() is { Length: > 0 } n ? n : null;
        request.Version++;
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException
                                   || (ex is DbUpdateException u && UniqueViolation(u) is { } i && i.Contains("OneDecision")))
        {
            await tx.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            return await AlreadyDecidedAsync(id, callerId, ChangeRequestStatus.Approved, ct);
        }

        // 2. The change itself, through the desk's own rules once more: between the check above and the
        // decision the world may have moved.
        var (ready, applyError, _) = await NewEmployee.PrepareAsync(_db, _hasher, payload.ToDeskRequest(), ct);
        if (ready is null)
        {
            await UndoAsync(tx, id, callerId, now, ct);
            return await RefuseApplyAsync(id, callerId, applyError!, now, ct);
        }
        await NewEmployee.AddAsync(_db, ready.Employee, ct);
        request.EmployeeId = ready.Employee.Id;
        Line(request, ChangeRequestEventKind.Approved, callerId, now, request.DecisionNote);
        _db.ChangeRequestEvents.Add(new ChangeRequestEvent
        {
            ChangeRequestId = request.Id,
            Kind = ChangeRequestEventKind.Applied,
            ActorEmployeeId = callerId,
            AtUtc = now,
            Data = JsonSerializer.Serialize(new { employeeId = ready.Employee.Id }, ApprovalGate.Json),
        });
        try
        {
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Two people given the same number in the same second, and similar. Nothing of either step stays.
            _logger.LogWarning(ex, "Approving request {Id} could not apply it", id);
            await UndoAsync(tx, id, callerId, now, ct);
            return await RefuseApplyAsync(id, callerId, ApplyErrorOf(ex), now, ct);
        }

        var branchName = scope!.Branch(request.LocationId)?.Name;
        // The temporary PIN is shown here, once, to the approver. It is never stored in readable form. If it is
        // lost, the branch gets a new one with «PIN sıfırla» as for anybody else.
        return new DeskResult(200, new
        {
            request = Summary(request, branchName),
            employeeId = ready.Employee.Id,
            tempPin = ready.TempPin,
        });
    }

    public async Task<DeskResult> RejectAsync(Guid callerId, Guid id, string? reason, CancellationToken ct)
    {
        var text = reason?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length < 3)
            return Bad("RejectReasonRequired");
        if (text.Length > MaxNote)
            return Bad("NoteTooLong");
        var now = Now;
        var (request, scope, refused) = await DecisionTargetAsync(callerId, id, ChangeRequestStatus.Rejected, now, ct);
        if (refused is not null)
            return refused;
        return await CloseAsync(request!, scope!, ChangeRequestStatus.Rejected, ChangeRequestEventKind.Rejected, callerId, text, now, ct);
    }

    public async Task<DeskResult> CancelAsync(Guid callerId, Guid id, string? note, CancellationToken ct)
    {
        var text = note?.Trim() is { Length: > 0 } t ? t : null;
        if (text is { Length: > MaxNote })
            return Bad("NoteTooLong");
        var now = Now;
        var request = await _db.ChangeRequests.FirstOrDefaultAsync(r => r.Id == id && !r.Direct, ct);
        var scope = await ApprovalScope.LoadAsync(_db, now, ct);
        if (request is null || !scope.MayView(callerId, request))
            return NotFound();
        if (request.Status != ChangeRequestStatus.Pending)
            return Decided(request, callerId, ChangeRequestStatus.Cancelled);
        // Withdrawing is the asker's alone. An approver who disagrees rejects, with a reason.
        if (request.RequestedByEmployeeId != callerId)
            return new DeskResult(403, new { error = "OnlyRequesterCanCancel" });
        return await CloseAsync(request, scope, ChangeRequestStatus.Cancelled, ChangeRequestEventKind.Cancelled, callerId, text, now, ct);
    }

    /// <summary>The request a decision is about, or the refusal. The same order for approve and reject: unknown or
    /// not visible (404, so nobody learns what exists elsewhere), already final, the asker themselves, then the
    /// approver's right, read now.</summary>
    private async Task<(ChangeRequest? Request, ApprovalScope? Scope, DeskResult? Refused)> DecisionTargetAsync(
        Guid callerId, Guid id, ChangeRequestStatus wanted, DateTime now, CancellationToken ct)
    {
        var request = await _db.ChangeRequests.FirstOrDefaultAsync(r => r.Id == id && !r.Direct, ct);
        var scope = await ApprovalScope.LoadAsync(_db, now, ct);
        if (request is null || !scope.MayView(callerId, request))
            return (null, null, NotFound());
        if (request.Status != ChangeRequestStatus.Pending)
            return (null, null, Decided(request, callerId, wanted));
        if (request.RequestedByEmployeeId == callerId)
            return (null, null, new DeskResult(403, new { error = "SelfDecisionNotAllowed" }));
        if (!scope.IsApprover(callerId, request.LocationId))
            return (null, null, new DeskResult(403, new { error = "NotApprover" }));
        return (request, scope, null);
    }

    private async Task<DeskResult> CloseAsync(
        ChangeRequest request, ApprovalScope scope, ChangeRequestStatus status, ChangeRequestEventKind kind,
        Guid callerId, string? note, DateTime now, CancellationToken ct)
    {
        if (!ChangeRequestRules.CanMove(request.Status, status))
            return Decided(request, callerId, status);
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        // The move first, on its own, against the version that was read; its history line once it is ours. One
        // transaction, so neither stays without the other.
        request.Status = status;
        request.DecidedByEmployeeId = callerId;
        request.DecidedAtUtc = now;
        request.DecisionNote = note;
        request.Version++;
        try
        {
            await _db.SaveChangesAsync(ct);
            Line(request, kind, callerId, now, note);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException
                                   || (ex is DbUpdateException u && UniqueViolation(u) is { } i && i.Contains("OneDecision")))
        {
            await tx.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            return await AlreadyDecidedAsync(request.Id, callerId, status, ct);
        }
        return new DeskResult(200, new { request = Summary(request, scope.Branch(request.LocationId)?.Name) });
    }

    /// <summary>Asked again for what is already so: the same person repeating the same decision gets the same
    /// answer (a retried press), anybody else is told it is decided.</summary>
    private static DeskResult Decided(ChangeRequest request, Guid callerId, ChangeRequestStatus wanted) =>
        request.Status == wanted && request.DecidedByEmployeeId == callerId
            ? new DeskResult(200, new { request = Summary(request, null, replayed: true) })
            : new DeskResult(409, new { error = "AlreadyDecided", status = request.Status });

    private async Task<DeskResult> AlreadyDecidedAsync(Guid id, Guid callerId, ChangeRequestStatus wanted, CancellationToken ct)
    {
        var now = await _db.ChangeRequests.AsNoTracking().FirstAsync(r => r.Id == id, ct);
        return now.Status == ChangeRequestStatus.Pending
            ? new DeskResult(409, new { error = "TryAgain" })
            : Decided(now, callerId, wanted);
    }

    /// <summary>The approval could not be applied: the transaction is rolled back, so the request is pending again
    /// and nobody's account exists. On a provider without transactions (the tests' in-memory store) the decision
    /// would have stayed. It is put back by hand, under the same conditions the rollback would have met.</summary>
    private async Task UndoAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx, Guid id, Guid callerId,
        DateTime decidedAt, CancellationToken ct)
    {
        await tx.RollbackAsync(ct);
        _db.ChangeTracker.Clear();
        var request = await _db.ChangeRequests.FirstAsync(r => r.Id == id, ct);
        if (request.Status != ChangeRequestStatus.Approved || request.DecidedByEmployeeId != callerId
            || request.DecidedAtUtc != decidedAt || request.EmployeeId != null)
            return;
        request.Status = ChangeRequestStatus.Pending;
        request.DecidedByEmployeeId = null;
        request.DecidedAtUtc = null;
        request.AppliedAtUtc = null;
        request.DecisionNote = null;
        request.Version++;
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
    }

    private async Task<DeskResult> RefuseApplyAsync(Guid id, Guid callerId, string error, DateTime now, CancellationToken ct)
    {
        _db.ChangeRequestEvents.Add(new ChangeRequestEvent
        {
            ChangeRequestId = id,
            Kind = ChangeRequestEventKind.ApplyRefused,
            ActorEmployeeId = callerId,
            AtUtc = now,
            Data = JsonSerializer.Serialize(new { error }, ApprovalGate.Json),
        });
        await _db.SaveChangesAsync(ct);
        return new DeskResult(409, new { error = "ApplyRefused", reason = error });
    }

    private static string ApplyErrorOf(DbUpdateException ex) => UniqueViolation(ex) switch
    {
        { } i when i.Contains("PhoneNumber", StringComparison.Ordinal) => "PhoneAlreadyExists",
        { } i when i.Contains("Email", StringComparison.Ordinal) => "EmailAlreadyExists",
        _ => "ApplyFailed",
    };

    // --- read ---------------------------------------------------------------------------------------------

    public async Task<DeskResult> ListAsync(
        Guid callerId, string? view, string? status, Guid? locationId, int? limit, CancellationToken ct)
    {
        ChangeRequestStatus? wanted = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<ChangeRequestStatus>(status, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                return Bad("UnknownStatus");
            wanted = parsed;
        }
        var take = Math.Clamp(limit ?? 100, 1, 500);
        var scope = await ApprovalScope.LoadAsync(_db, Now, ct);
        IQueryable<ChangeRequest> query = _db.ChangeRequests.AsNoTracking();

        switch (view?.ToLowerInvariant())
        {
            case "queue":
                // What waits for me: pending, at a branch I decide for, made by somebody else. Oldest first.
                var mine = scope.ApproverBranches(callerId).ToList();
                query = query.Where(r => r.Status == ChangeRequestStatus.Pending && !r.Direct
                                         && r.RequestedByEmployeeId != callerId && mine.Contains(r.LocationId))
                    .OrderBy(r => r.CreatedAtUtc);
                break;
            case "mine":
                query = query.Where(r => r.RequestedByEmployeeId == callerId && !r.Direct)
                    .OrderByDescending(r => r.CreatedAtUtc);
                break;
            case null or "" or "all":
                var visible = scope.VisibleBranches(callerId)?.ToList();
                if (visible is not null)
                    query = query.Where(r => r.RequestedByEmployeeId == callerId || visible.Contains(r.LocationId));
                query = query.OrderByDescending(r => r.CreatedAtUtc);
                break;
            default:
                return Bad("UnknownView");
        }
        if (wanted is { } s)
            query = query.Where(r => r.Status == s);
        if (locationId is Guid l)
            query = query.Where(r => r.LocationId == l);

        var rows = await query.Take(take).ToListAsync(ct);
        var names = await NamesAsync(rows.SelectMany(r => new[] { r.RequestedByEmployeeId, r.DecidedByEmployeeId ?? Guid.Empty }), ct);
        var now = Now;
        return new DeskResult(200, new
        {
            items = rows.Select(r => Item(r, scope, names, callerId, now)),
        });
    }

    public async Task<DeskResult> DetailAsync(Guid callerId, Guid id, CancellationToken ct)
    {
        var request = await _db.ChangeRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        var scope = await ApprovalScope.LoadAsync(_db, Now, ct);
        if (request is null || !scope.MayView(callerId, request))
            return NotFound();
        var lines = await _db.ChangeRequestEvents.AsNoTracking()
            .Where(e => e.ChangeRequestId == id)
            .OrderBy(e => e.AtUtc).ThenBy(e => e.Kind)
            .ToListAsync(ct);
        var names = await NamesAsync(
            lines.Select(e => e.ActorEmployeeId ?? Guid.Empty)
                .Append(request.RequestedByEmployeeId).Append(request.DecidedByEmployeeId ?? Guid.Empty), ct);
        var seesContacts = await SeesContactsAsync(callerId, request, scope, ct);
        return new DeskResult(200, new
        {
            request = Item(request, scope, names, callerId, Now),
            payload = PayloadFor(request, seesContacts),
            // The screen says «gizlədilib» rather than showing an empty field as if nothing had been written.
            contactsHidden = !seesContacts,
            note = request.Note,
            decisionNote = request.DecisionNote,
            employeeId = request.EmployeeId,
            history = lines.Select(e => new
            {
                kind = e.Kind,
                atUtc = e.AtUtc,
                actor = e.ActorEmployeeId is Guid a ? names.GetValueOrDefault(a) : null,
                note = e.Note,
                data = e.Data is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(e.Data),
            }),
        });
    }

    /// <summary>
    /// Whether the caller reads the person's phone number, e-mail and birth date on this request. The line is the
    /// staff card's (ManagerController.Employee, EnforcedRows.SeesContacts): «employees.contacts.view» over the
    /// branch, because a phone number or an e-mail here is half of somebody's login. Seeing a request is not
    /// enough. An observer holds «requests.view» and reads the name, the branch and the history, never the
    /// numbers.
    ///
    /// Two more people read them, because the request is theirs. The person who asked wrote them. The person
    /// deciding a pending request must see what they approve: the structure's operator, or an operator standing
    /// in, who may hold no contact right over that branch otherwise. Once decided, the ordinary line applies.
    ///
    /// If the account the request made has since become somebody above plain staff, only the person and the
    /// Company administrator read them, as on the card.
    /// </summary>
    private async Task<bool> SeesContactsAsync(Guid callerId, ChangeRequest request, ApprovalScope scope, CancellationToken ct)
    {
        var access = scope.AccessOf(callerId);
        if (request.EmployeeId is Guid made)
        {
            if (made == callerId)
                return true;
            var role = await _db.Employees.AsNoTracking()
                .Where(e => e.Id == made).Select(e => (EmployeeRole?)e.Role).FirstOrDefaultAsync(ct);
            if (role is not null and not EmployeeRole.Employee)
                return AccessProjection.IsCompanyAdmin(access);
        }
        if (request.RequestedByEmployeeId == callerId)
            return true;
        if (request.Status == ChangeRequestStatus.Pending && !request.Direct && scope.IsApprover(callerId, request.LocationId))
            return true;
        return access.Covers(AccessPermissions.EmployeesContactsView, request.LocationId);
    }

    /// <summary>The request's payload as this caller may read it. Without the contact right, the phone number, the
    /// e-mail and the birth date are removed on the server, never merely hidden by the screen. A type this code
    /// does not know how to redact is not shown at all to somebody who may not read contacts: closed by default.</summary>
    internal static JsonElement? PayloadFor(ChangeRequest request, bool seesContacts)
    {
        if (request.Type == ChangeRequestType.EmployeeCreate
            && JsonSerializer.Deserialize<EmployeeCreatePayload>(request.Payload, ApprovalGate.Json) is { } person)
            return JsonSerializer.SerializeToElement(
                seesContacts ? person : person with { PhoneNumber = null, Email = null, BirthDate = null },
                ApprovalGate.Json);
        return seesContacts ? JsonSerializer.Deserialize<JsonElement>(request.Payload) : null;
    }

    /// <summary>What the caller can do where requests are on, branch by branch: for the screen to offer «Müraciət
    /// göndər» instead of «Saxla», and to show the queue.</summary>
    public async Task<DeskResult> MeAsync(Guid callerId, CancellationToken ct)
    {
        var scope = await ApprovalScope.LoadAsync(_db, Now, ct);
        var structureNames = await _db.Structures.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        // Every branch in an area the caller has anything to do with: asks, decides, acts directly, or only
        // reads (an observer). The screen picks the person's role from these flags; nothing here widens a right.
        var branches = scope.Branches
            .Where(b => scope.InArea(b.Id))
            .Select(b => new
            {
                locationId = b.Id,
                name = b.Name,
                structureId = b.StructureId,
                structureName = b.StructureId is Guid s ? structureNames.GetValueOrDefault(s) : null,
                canRequest = scope.MayRequest(callerId, b.Id),
                canActDirectly = scope.MayActDirectly(callerId, b.Id),
                isApprover = scope.IsApprover(callerId, b.Id),
                standsIn = scope.StandsIn(callerId, b.Id),
                canView = scope.MayViewBranch(callerId, b.Id),
            })
            .Where(b => b.canRequest || b.canActDirectly || b.isApprover || b.canView)
            .OrderBy(b => b.structureName).ThenBy(b => b.name)
            .ToList();
        var queue = scope.ApproverBranches(callerId).ToList();
        var waiting = queue.Count == 0 ? 0 : await _db.ChangeRequests.CountAsync(r =>
            r.Status == ChangeRequestStatus.Pending && !r.Direct && r.RequestedByEmployeeId != callerId
            && queue.Contains(r.LocationId), ct);
        return new DeskResult(200, new { enabled = true, branches, queueCount = waiting });
    }

    /// <summary>
    /// What the «Yeni işçi» form offers at one branch: the job titles of the catalogue and the shifts the branch may
    /// use (its own and the company's). Asked of this desk rather than the manager desk because whoever may ask here
    /// is a timekeeper, who in a Legacy company may be plain staff by today's role, and the manager desk is closed to
    /// them. Answered only for a branch where the caller may ask or decide.
    /// </summary>
    public async Task<DeskResult> OptionsAsync(Guid callerId, Guid locationId, CancellationToken ct)
    {
        var scope = await ApprovalScope.LoadAsync(_db, Now, ct);
        if (scope.Branch(locationId) is null)
            return new DeskResult(404, new { error = "LocationNotFound" });
        if (!scope.MayRequest(callerId, locationId) && !scope.IsApprover(callerId, locationId))
            return new DeskResult(403, new { error = "RequestNotAllowed" });
        var positions = await _db.JobPositions.AsNoTracking().OrderBy(p => p.Name).Select(p => p.Name).ToListAsync(ct);
        var schedules = (await _db.Schedules.AsNoTracking().Templates()
                .Where(s => s.LocationId == null || s.LocationId == locationId)
                .OrderBy(s => s.Name)
                .Select(s => new { s.Id, s.Name, s.ShiftStart, s.ShiftEnd, s.LocationId })
                .ToListAsync(ct))
            .Select(s => new
            {
                id = s.Id, name = s.Name, start = s.ShiftStart.ToString("HH:mm"), end = s.ShiftEnd.ToString("HH:mm"),
                branchOnly = s.LocationId != null,
            });
        return new DeskResult(200, new { positions, schedules });
    }

    private object Item(ChangeRequest r, ApprovalScope scope, IReadOnlyDictionary<Guid, string> names, Guid callerId, DateTime now)
    {
        var pending = r.Status == ChangeRequestStatus.Pending;
        return new
        {
            id = r.Id,
            number = r.Number,
            type = r.Type,
            status = r.Status,
            direct = r.Direct,
            locationId = r.LocationId,
            locationName = scope.Branch(r.LocationId)?.Name,
            subject = SubjectOf(r),
            requestedBy = names.GetValueOrDefault(r.RequestedByEmployeeId),
            requestedByMe = r.RequestedByEmployeeId == callerId,
            createdAtUtc = r.CreatedAtUtc,
            decidedBy = r.DecidedByEmployeeId is Guid d ? names.GetValueOrDefault(d) : null,
            decidedAtUtc = r.DecidedAtUtc,
            // The rejection's reason, so the person who asked reads it in their list without opening the request.
            decisionNote = r.DecisionNote,
            waitingOver24h = pending && now - r.CreatedAtUtc > TimeSpan.FromHours(24),
            canDecide = pending && !r.Direct && r.RequestedByEmployeeId != callerId && scope.IsApprover(callerId, r.LocationId),
            canCancel = pending && !r.Direct && r.RequestedByEmployeeId == callerId,
        };
    }

    /// <summary>One line naming what the request is about, for a list row.</summary>
    internal static string SubjectOf(ChangeRequest r)
    {
        try
        {
            return r.Type switch
            {
                ChangeRequestType.EmployeeCreate =>
                    JsonSerializer.Deserialize<EmployeeCreatePayload>(r.Payload, ApprovalGate.Json)?.FullName.Trim() ?? "",
                _ => "",
            };
        }
        catch (JsonException)
        {
            return "";
        }
    }

    private static object Summary(ChangeRequest r, string? branchName, bool replayed = false) => new
    {
        id = r.Id,
        number = r.Number,
        type = r.Type,
        status = r.Status,
        locationId = r.LocationId,
        locationName = branchName,
        subject = SubjectOf(r),
        createdAtUtc = r.CreatedAtUtc,
        decidedAtUtc = r.DecidedAtUtc,
        replayed,
    };

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var wanted = ids.Where(i => i != Guid.Empty).Distinct().ToList();
        if (wanted.Count == 0)
            return [];
        return await _db.Employees.AsNoTracking()
            .Where(e => wanted.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
    }

    private ChangeRequestEvent Line(ChangeRequest request, ChangeRequestEventKind kind, Guid actorId, DateTime now, string? note)
    {
        var line = new ChangeRequestEvent
        {
            ChangeRequestId = request.Id,
            Kind = kind,
            ActorEmployeeId = actorId,
            AtUtc = now,
            Note = note,
        };
        _db.ChangeRequestEvents.Add(line);
        return line;
    }

    internal static string? UniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
            ? pg.ConstraintName ?? string.Empty
            : null;

    private static DeskResult Bad(string error) => new(400, new { error });

    private static DeskResult NotFound() => new(404, new { error = "RequestNotFound" });
}

/// <summary>The state machine. Pending moves once, to one of the three final states. Nothing moves out of a
/// final state.</summary>
public static class ChangeRequestRules
{
    public static bool CanMove(ChangeRequestStatus from, ChangeRequestStatus to) =>
        from == ChangeRequestStatus.Pending && to is ChangeRequestStatus.Approved or ChangeRequestStatus.Rejected
            or ChangeRequestStatus.Cancelled;
}
