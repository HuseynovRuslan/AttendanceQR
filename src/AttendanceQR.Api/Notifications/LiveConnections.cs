using System.Collections.Concurrent;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Notifications;

/// <summary>
/// Every open live connection, with the session it was opened under: company, person and the token version that session
/// held. Two things follow from keeping it:
///   • a send reaches only the connections whose session is still the person's current one — checked against the
///     database just before the send (<see cref="LiveSessions"/>), so a session revoked a second ago hears nothing more;
///   • the sweep (NotificationWorker, every few seconds) closes the others as soon as the session ends — a PIN reset,
///     «hər yerdən çıxış», a deactivated account, a suspended company — without waiting for the connection to expire.
/// There is no hook in the places that end a session (there are fifteen of them, and the sixteenth would be forgotten):
/// the state itself — Employee.TokenVersion, IsActive, Tenant.IsActive — is read, the same three things the JWT check reads.
/// One backend process, so memory is enough (as for <see cref="HubTickets"/>).
/// </summary>
public sealed class LiveConnections
{
    public sealed record Session(Guid TenantId, Guid EmployeeId, int TokenVersion);

    private sealed record Entry(Session Session, Action Abort);

    private readonly ConcurrentDictionary<string, Entry> _connections = new();

    public int Count => _connections.Count;

    public void Add(string connectionId, Session session, Action abort) => _connections[connectionId] = new(session, abort);

    public void Remove(string connectionId) => _connections.TryRemove(connectionId, out _);

    /// <summary>The companies with an open connection, each with the people connected — what the sweep has to check.</summary>
    public IReadOnlyDictionary<Guid, IReadOnlyCollection<Guid>> Connected() =>
        _connections.Values
            .GroupBy(e => e.Session.TenantId)
            .ToDictionary(g => g.Key, g => (IReadOnlyCollection<Guid>)g.Select(e => e.Session.EmployeeId).Distinct().ToList());

    /// <summary>
    /// The connections that may hear this person's notifications now: opened in this company, by this person, under the
    /// token version the person holds today. <paramref name="currentVersion"/> null = no valid session at all (account
    /// deactivated, company suspended): nothing is returned. Any connection of theirs that does not qualify is closed
    /// on the way, so it does not linger until the sweep.
    /// </summary>
    public IReadOnlyList<string> Current(Guid tenantId, Guid employeeId, int? currentVersion)
    {
        var current = new List<string>();
        foreach (var (id, entry) in _connections)
        {
            if (entry.Session.TenantId != tenantId || entry.Session.EmployeeId != employeeId)
                continue;
            if (currentVersion == entry.Session.TokenVersion)
                current.Add(id);
            else
                Close(id, entry);
        }
        return current;
    }

    /// <summary>
    /// Closes every connection in the company whose session is not current. <paramref name="sessions"/> holds the people
    /// with a valid session (active account in an active company) and their token version now; anyone connected but
    /// missing from it — or connected under another version — is closed. Returns how many were closed.
    /// </summary>
    public int CloseStale(Guid tenantId, IReadOnlyDictionary<Guid, int> sessions)
    {
        var closed = 0;
        foreach (var (id, entry) in _connections)
        {
            if (entry.Session.TenantId != tenantId)
                continue;
            if (sessions.TryGetValue(entry.Session.EmployeeId, out var version) && version == entry.Session.TokenVersion)
                continue;
            Close(id, entry);
            closed++;
        }
        return closed;
    }

    private void Close(string id, Entry entry)
    {
        if (!_connections.TryRemove(new KeyValuePair<string, Entry>(id, entry)))
            return;   // somebody else closed it first
        try { entry.Abort(); }
        catch { /* already gone — nothing to close */ }
    }
}

/// <summary>Who may hear live notifications right now, read from the same state the JWT check reads.</summary>
public static class LiveSessions
{
    private static readonly IReadOnlyDictionary<Guid, int> None = new Dictionary<Guid, int>();

    /// <summary>
    /// For each of <paramref name="people"/> with a valid session — an active account in an active company — the token
    /// version the session must carry. Anyone else is absent. The caller has resolved <paramref name="tenantId"/>.
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, int>> ReadAsync(
        AppDbContext db, Guid tenantId, IReadOnlyCollection<Guid> people, CancellationToken ct)
    {
        if (people.Count == 0)
            return None;
        if (!await db.Tenants.AnyAsync(t => t.Id == tenantId && t.IsActive, ct))
            return None;
        return await db.Employees
            .Where(e => people.Contains(e.Id) && e.IsActive)
            .Select(e => new { e.Id, e.TokenVersion })
            .ToDictionaryAsync(e => e.Id, e => e.TokenVersion, ct);
    }
}
