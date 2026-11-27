using System.Globalization;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Notifications;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Notifications;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services.Access;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// The notification center («Bildirişlər») and the person's own notification settings («Bildiriş ayarları»).
///
/// Everything here is the caller's own: their lines, their settings. Two rules are enforced on the server whatever the
/// screen shows:
///   • the center lists a line only while the caller may still see its branch (<see cref="NotificationAudience"/>, read
///     per request) — a manager moved off a branch stops seeing its history as well as its new events;
///   • a setting can name only branches inside the caller's scope, and switching a type on needs some scope at all —
///     a plain employee cannot subscribe to colleagues' attendance.
/// The company is the request's tenant (query filters), so nothing here can reach another company's rows.
/// </summary>
[ApiController]
[Authorize]
[Route("api/notifications")]
public class NotificationsController : ControllerBase
{
    private const int DefaultPage = 25;
    private const int MaxPage = 50;
    private const int UnreadCap = 99;

    /// <summary>«Hamısını oxunmuş et» marks this many at a time, so a reader with a year of lines is many short
    /// statements, not one that outlives the request.</summary>
    private const int MarkBatch = 5000;

    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly HubTickets? _tickets;

    public NotificationsController(AppDbContext db, IMemoryCache cache, HubTickets? tickets = null)
    {
        _db = db;
        _cache = cache;
        _tickets = tickets;
    }

    private Guid Me => User.EmployeeId();

    // --- the center -------------------------------------------------------------------------------------------

    /// <summary>
    /// GET /api/notifications?type=&amp;locationId=&amp;unread=&amp;cursor=&amp;limit= — newest first, a page at a time. The
    /// cursor is the last line's (createdAt, id), so a page never repeats or skips a line when new ones arrive.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? type, [FromQuery] Guid? locationId, [FromQuery] bool? unread,
        [FromQuery] string? cursor, [FromQuery] int? limit)
    {
        var ct = HttpContext.RequestAborted;
        var take = Math.Clamp(limit ?? DefaultPage, 1, MaxPage);
        var query = await VisibleAsync(ct);

        if (!string.IsNullOrWhiteSpace(type))
        {
            if (NotificationTypes.Find(type) is null)
                return BadRequest(new { error = "UnknownType" });
            query = query.Where(n => n.TypeKey == type);
        }
        if (locationId is Guid branch)
            query = query.Where(n => n.LocationId == branch);
        if (unread == true)
            query = query.Where(n => n.ReadAtUtc == null);
        if (cursor is not null)
        {
            if (!TryParseCursor(cursor, out var at, out var id))
                return BadRequest(new { error = "BadCursor" });
            query = query.Where(n => n.CreatedAtUtc < at || (n.CreatedAtUtc == at && n.Id.CompareTo(id) < 0));
        }

        var page = await query
            .OrderByDescending(n => n.CreatedAtUtc).ThenByDescending(n => n.Id)
            .Take(take + 1)
            .Select(n => new NotificationView(
                n.Id, n.TypeKey, n.Title, n.Body, n.SubjectEmployeeId, n.SubjectName, n.LocationId, n.BranchName,
                n.OccurredAtUtc, n.WasOffline, n.Minutes, n.Url, n.CreatedAtUtc, n.ReadAtUtc))
            .ToListAsync(ct);

        var more = page.Count > take;
        var items = more ? page.Take(take).ToList() : page;
        var last = items.LastOrDefault();
        return Ok(new
        {
            items,
            nextCursor = more && last is not null ? Cursor(last.CreatedAtUtc, last.Id) : null,
        });
    }

    /// <summary>GET /api/notifications/unread-count — the badge.</summary>
    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount()
    {
        var ct = HttpContext.RequestAborted;
        var query = await VisibleAsync(ct);
        // Counted only as far as the badge can show («99+»): a company-wide reader at a large company collects tens of
        // thousands of lines a day, and this runs every minute for every open panel.
        var unread = await query.Where(n => n.ReadAtUtc == null).Take(UnreadCap + 1).CountAsync(ct);
        return Ok(new { unread = Math.Min(unread, UnreadCap), more = unread > UnreadCap });
    }

    /// <summary>POST /api/notifications/{id}/read — one line read. Only the caller's own.</summary>
    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id)
    {
        var ct = HttpContext.RequestAborted;
        var line = await _db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.RecipientEmployeeId == Me, ct);
        if (line is null)
            return NotFound(new { error = "NotFound" });
        line.ReadAtUtc ??= DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(new { id, readAtUtc = line.ReadAtUtc });
    }

    /// <summary>POST /api/notifications/read-all — every unread line the caller can see, optionally one type.</summary>
    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead([FromQuery] string? type)
    {
        var ct = HttpContext.RequestAborted;
        // Tracked: the in-memory fallback below changes the rows it loads.
        var query = (await VisibleAsync(ct, tracked: true)).Where(n => n.ReadAtUtc == null);
        if (!string.IsNullOrWhiteSpace(type))
            query = query.Where(n => n.TypeKey == type);

        var now = DateTime.UtcNow;
        // Only what was there when the reader pressed it: lines arriving meanwhile stay unread.
        query = query.Where(n => n.CreatedAtUtc <= now);
        var count = 0;
        if (_db.Database.IsRelational())
        {
            for (var round = 0; round < 400; round++)
            {
                var marked = await query.OrderBy(n => n.Id).Take(MarkBatch)
                    .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAtUtc, now), ct);
                count += marked;
                if (marked < MarkBatch)
                    break;
            }
        }
        else
        {
            // The in-memory test store has no ExecuteUpdate.
            var lines = await query.ToListAsync(ct);
            foreach (var line in lines)
                line.ReadAtUtc = now;
            await _db.SaveChangesAsync(ct);
            count = lines.Count;
        }
        return Ok(new { marked = count });
    }

    /// <summary>
    /// POST /api/notifications/stream-ticket — a two-minute ticket for the live channel (<see cref="NotificationsHub"/>).
    /// A POST, so a view-only session and an impersonating operator get none (their boundaries refuse writes): they
    /// fall back to polling, and no live channel ever carries a borrowed session.
    /// </summary>
    [HttpPost("stream-ticket")]
    public IActionResult StreamTicket()
    {
        if (_tickets is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "LiveUnavailable" });
        return Ok(new { ticket = _tickets.Issue(_db.CurrentTenantId, Me), expiresInSeconds = (int)HubTickets.Lifetime.TotalSeconds });
    }

    // --- settings ---------------------------------------------------------------------------------------------

    /// <summary>GET /api/notifications/preferences?category=attendance — the settings page's whole state.</summary>
    [HttpGet("preferences")]
    public async Task<IActionResult> GetPreferences([FromQuery] string? category)
    {
        var cat = category ?? NotificationTypes.CategoryAttendance;
        if (!NotificationTypes.Categories.Contains(cat))
            return BadRequest(new { error = "UnknownCategory" });
        return Ok(await PreferencesViewAsync(cat, HttpContext.RequestAborted));
    }

    /// <summary>PUT /api/notifications/preferences — saves one category's settings, whole.</summary>
    [HttpPut("preferences")]
    public async Task<IActionResult> SavePreferences([FromBody] NotificationPreferencesRequest request)
    {
        try
        {
            return await SavePreferencesAsync(request);
        }
        catch (DbUpdateException ex) when (Approvals.ChangeRequestDesk.UniqueViolation(ex) is not null)
        {
            // Two saves of a first-ever setting at once (a double tap, two tabs): the other one created the row this
            // one tried to create. Once more, now finding it — the later save wins, as it does for any other edit.
            _db.ChangeTracker.Clear();
            return await SavePreferencesAsync(request);
        }
    }

    private async Task<IActionResult> SavePreferencesAsync(NotificationPreferencesRequest request)
    {
        var ct = HttpContext.RequestAborted;
        var cat = request.Category ?? NotificationTypes.CategoryAttendance;
        if (!NotificationTypes.Categories.Contains(cat))
            return BadRequest(new { error = "UnknownCategory" });

        var requested = (request.EnabledTypes ?? []).Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().ToList();
        var types = NotificationTypes.Normalize(cat, requested);
        if (types.Count != requested.Count)
            return BadRequest(new { error = "UnknownType" });

        var mode = request.BranchMode?.Trim().ToLowerInvariant() switch
        {
            null or "" or "all" => NotificationBranchMode.AllPermitted,
            "selected" => NotificationBranchMode.Selected,
            _ => (NotificationBranchMode?)null,
        };
        if (mode is null)
            return BadRequest(new { error = "UnknownBranchMode" });

        var scope = (await NotificationAudience.LoadAsync(_db, [Me], ct)).For(Me);
        if (types.Count > 0 && !scope.Any)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "NotEligible" });
        if (types.Count > 0 && !request.InApp && !request.Push && !request.Web)
            return BadRequest(new { error = "NoChannel" });

        var picked = (request.LocationIds ?? []).Distinct().ToList();
        if (mode == NotificationBranchMode.Selected)
        {
            if (types.Count > 0 && picked.Count == 0)
                return BadRequest(new { error = "NoBranchSelected" });
            var known = await _db.Locations.AsNoTracking().Where(l => picked.Contains(l.Id)).Select(l => l.Id).ToListAsync(ct);
            if (known.Count != picked.Count || picked.Any(id => !scope.Covers(id)))
                return BadRequest(new { error = "BranchOutOfScope" });
        }

        var row = await _db.NotificationPreferences.FirstOrDefaultAsync(p => p.EmployeeId == Me && p.Category == cat, ct);
        if (row is null)
        {
            row = new NotificationPreference { EmployeeId = Me, Category = cat };
            _db.NotificationPreferences.Add(row);
        }
        row.EnabledTypes = NotificationTypes.ToCsv(types);
        row.BranchMode = mode.Value;
        row.InApp = request.InApp;
        row.Push = request.Push;
        row.Web = request.Web;
        row.UpdatedAtUtc = DateTime.UtcNow;

        // The pick is replaced whole; kept when switching to «all», so switching back restores it.
        if (mode == NotificationBranchMode.Selected)
        {
            var old = await _db.NotificationBranchPreferences.Where(b => b.EmployeeId == Me && b.Category == cat).ToListAsync(ct);
            _db.NotificationBranchPreferences.RemoveRange(old.Where(b => !picked.Contains(b.LocationId)));
            foreach (var id in picked.Where(id => old.All(b => b.LocationId != id)))
                _db.NotificationBranchPreferences.Add(new NotificationBranchPreference { EmployeeId = Me, Category = cat, LocationId = id });
        }

        await _db.SaveChangesAsync(ct);
        // The scan's «does anybody here want this» answer is cached; switching on must take effect at once.
        NotificationOutbox.Forget(_cache, _db.CurrentTenantId);
        return Ok(await PreferencesViewAsync(cat, ct));
    }

    private async Task<object> PreferencesViewAsync(string category, CancellationToken ct)
    {
        var scope = (await NotificationAudience.LoadAsync(_db, [Me], ct)).For(Me);
        var row = await _db.NotificationPreferences.AsNoTracking().FirstOrDefaultAsync(p => p.EmployeeId == Me && p.Category == category, ct);
        var enabled = row is null ? new HashSet<string>() : NotificationTypes.Normalize(category, NotificationTypes.ParseCsv(row.EnabledTypes)).ToHashSet();

        // The branches the person may pick from: active ones inside their scope, grouped by structure on the page.
        var structures = await _db.Structures.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var branches = (await _db.Locations.AsNoTracking()
                .Where(l => l.IsActive)
                .OrderBy(l => l.Name)
                .Select(l => new { l.Id, l.Name, l.StructureId })
                .ToListAsync(ct))
            .Where(l => scope.Covers(l.Id))
            .Select(l => new
            {
                id = l.Id,
                name = l.Name,
                structureId = l.StructureId,
                structureName = l.StructureId is Guid s && structures.TryGetValue(s, out var n) ? n : null,
            })
            .ToList();
        var visible = branches.Select(b => b.id).ToHashSet();
        var selected = row is null ? new List<Guid>() : (await _db.NotificationBranchPreferences.AsNoTracking()
                .Where(b => b.EmployeeId == Me && b.Category == category)
                .Select(b => b.LocationId)
                .ToListAsync(ct))
            // A branch the person can no longer see drops out of the pick on the page, as it does at sending.
            .Where(visible.Contains)
            .ToList();

        return new
        {
            category,
            eligible = scope.Any,
            companyWide = scope.Company,
            types = NotificationTypes.InCategory(category).Select(d => new
            {
                key = d.Key,
                label = d.Label,
                description = d.Description,
                enabled = enabled.Contains(d.Key),
            }),
            branchMode = row?.BranchMode == NotificationBranchMode.Selected ? "selected" : "all",
            locationIds = selected,
            channels = new { inApp = row?.InApp ?? true, push = row?.Push ?? true, web = row?.Web ?? true },
            branches,
            updatedAtUtc = row?.UpdatedAtUtc,
        };
    }

    /// <summary>The caller's lines whose branch they may see now. A line with no branch is always theirs to see.</summary>
    private async Task<IQueryable<Notification>> VisibleAsync(CancellationToken ct, bool tracked = false)
    {
        var me = Me;
        var scope = (await NotificationAudience.LoadAsync(_db, [me], ct)).For(me);
        var lines = tracked ? _db.Notifications : _db.Notifications.AsNoTracking();
        var query = lines.Where(n => n.RecipientEmployeeId == me);
        if (scope.Company)
            return query;
        var branches = scope.Branches.ToList();
        return query.Where(n => n.LocationId == null || branches.Contains(n.LocationId.Value));
    }

    private static string Cursor(DateTime at, Guid id) =>
        $"{at.Ticks.ToString(CultureInfo.InvariantCulture)}_{id:N}";

    private static bool TryParseCursor(string cursor, out DateTime at, out Guid id)
    {
        at = default;
        id = default;
        var parts = cursor.Split('_');
        if (parts.Length != 2 || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks || !Guid.TryParseExact(parts[1], "N", out id))
            return false;
        at = new DateTime(ticks, DateTimeKind.Utc);
        return true;
    }
}

public sealed record NotificationView(
    Guid Id, string TypeKey, string Title, string Body, Guid? SubjectEmployeeId, string? SubjectName, Guid? LocationId,
    string? BranchName, DateTime OccurredAtUtc, bool WasOffline, int? Minutes, string? Url, DateTime CreatedAtUtc,
    DateTime? ReadAtUtc);
