using AttendanceQR.Domain.Access;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Infrastructure.Services.Access;

/// <summary>
/// The permission model's per-person answers for a whole list, in a company switched to Enforced — worked out in
/// memory from one graph, never a query per row. <see cref="LoadAsync"/> returns null in Legacy and Shadow, and
/// the list keeps today's answers exactly as its own code writes them.
///
/// Today's lists lean on the target's ROLE — plain staff, a fellow manager — because that is all a manager could
/// meet. In Enforced the caller may be a timekeeper and the target an operator: the model's rules
/// (ShadowRules.ManageableEmployee, CredentialTarget) are the ones that decide the write, so they are the ones
/// the row shows; and a phone number or an e-mail rides only where the caller holds «employees.contacts.view».
/// </summary>
public sealed class EnforcedRows
{
    private readonly AccessGraph _graph;
    private readonly IReadOnlySet<Guid> _operators;
    private readonly Guid? _myHome;

    private EnforcedRows(AccessGraph graph, EffectiveAccess me, Guid? myHome, IReadOnlySet<Guid> operators)
    {
        _graph = graph;
        Me = me;
        _myHome = myHome;
        _operators = operators;
    }

    public EffectiveAccess Me { get; }

    public static async Task<EnforcedRows?> LoadAsync(AppDbContext db, Guid callerId, CancellationToken ct = default)
    {
        if (db.AuthzObserver is not { } observer || await observer.ModeAsync(db, ct) != AuthzMode.Enforced)
            return null;
        var graph = await observer.GraphAsync(db, ct);
        var home = await db.Employees.AsNoTracking()
            .Where(e => e.Id == callerId).Select(e => (Guid?)e.LocationId).FirstOrDefaultAsync(ct);
        return new EnforcedRows(graph, graph.For(callerId), home, observer.OperatorIds);
    }

    /// <summary>One of ShadowRules' rules, for the person <paramref name="id"/> whose home branch is <paramref name="home"/>.</summary>
    public bool Allows(string rule, Guid id, Guid home) =>
        ShadowRules.Decide(rule, Me, id, home, _graph.For(id), _operators.Contains(id), _myHome);

    /// <summary>One's own always; anybody else's only with «employees.contacts.view» at their branch, and only on a
    /// row the caller may act on — a colleague's or a superior's phone is half their login.</summary>
    public bool SeesContacts(Guid id, Guid home) =>
        id == Me.EmployeeId
        || (Me.Covers(AccessPermissions.EmployeesContactsView, home) && Allows(ShadowRules.ManageableEmployee, id, home));
}
