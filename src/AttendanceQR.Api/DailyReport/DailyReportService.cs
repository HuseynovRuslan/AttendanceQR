using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AttendanceQR.Application.Common;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Application.Reporting.DailyReport;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.DailyReport;

public enum DeliveryKind { Scheduled = 0, Manual = 1 }
public enum DeliveryStatus { Pending = 0, Sent = 1, Failed = 2 }

public sealed record SendOutcome(Guid DeliveryId, Guid RecipientId, string RecipientName, bool Sent, string? Error);

/// <summary>
/// The daily management report, end to end, for the CURRENT (resolved) company: build the figures, freeze
/// them into a delivery, print the PDF, send it to Telegram with a link button, retry what failed, and
/// serve the page behind the link. Every query goes through the tenant filter — this class never reads
/// another company's rows, and a link only opens on its own company's host.
/// </summary>
public sealed class DailyReportService
{
    /// <summary>How late a scheduled send may still go out. A report for 18:00 delivered at 23:00 would be news from another shift.</summary>
    public static readonly TimeSpan SendWindow = TimeSpan.FromHours(3);
    private static readonly TimeSpan[] Backoff =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(30)];
    private static readonly TimeSpan ConnectLinkLife = TimeSpan.FromHours(48);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _db;
    private readonly IReportQueryService _reports;
    private readonly ITelegramClient _telegram;
    private readonly IPdfRenderer _pdf;
    private readonly DailyReportOptions _options;
    private readonly ITenantContext _tenant;
    private readonly TimeZoneInfo _zone;
    private readonly TimeProvider _clock;
    private readonly ILogger<DailyReportService> _logger;

    public DailyReportService(AppDbContext db, IReportQueryService reports, ITelegramClient telegram, IPdfRenderer pdf,
        DailyReportOptions options, AppOptions app, ITenantContext tenant, ILogger<DailyReportService> logger,
        TimeProvider? clock = null)
    {
        _db = db;
        _reports = reports;
        _telegram = telegram;
        _pdf = pdf;
        _options = options;
        _tenant = tenant;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
        _zone = TimeZoneInfo.FindSystemTimeZoneById(app.TimeZone);
    }

    private DateTime NowUtc() => _clock.GetUtcNow().UtcDateTime;
    private DateTime NowLocal() => TimeZoneInfo.ConvertTimeFromUtc(NowUtc(), _zone);

    // ───────────────────────────── the figures ─────────────────────────────

    /// <summary>Today's figures for one recipient's branches (null recipient / no branches = the whole company).</summary>
    public async Task<DailyReportData> BuildDataAsync(string? locationIdsCsv, CancellationToken ct)
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstAsync(t => t.Id == _tenant.TenantId, ct);
        var rows = await _reports.GetBoardForCompanyAsync(null, ct);
        var structures = await _db.Structures.AsNoTracking()
            .Where(s => s.ArchivedAtUtc == null)
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var locations = (await _db.Locations.AsNoTracking()
                .Where(l => l.IsActive)
                .Select(l => new { l.Id, l.Name, l.Headcount, l.StructureId })
                .ToListAsync(ct))
            .Select(l => new DailyReportLocation(l.Id, l.Name, l.Headcount,
                l.StructureId is Guid sid ? structures.GetValueOrDefault(sid) : null))
            .ToList();
        var order = DaySummaryTemplates.For(tenant.Id).FirstOrDefault()?.Areas;
        var nowLocal = NowLocal();
        return DailyReportBuilder.Build(tenant.Name, DateOnly.FromDateTime(nowLocal), nowLocal, rows, locations,
            ParseScope(locationIdsCsv), order);
    }

    public static IReadOnlySet<Guid>? ParseScope(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return null;
        var set = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => Guid.TryParse(x, out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty)
            .ToHashSet();
        return set.Count == 0 ? null : set;
    }

    public static string Caption(DailyReportData d)
    {
        var t = d.Totals;
        var sb = new StringBuilder();
        sb.Append("📊 <b>QRLog — Gündəlik davamiyyət</b>\n")
          .Append("🏢 ").Append(WebUtility.HtmlEncode(d.CompanyName)).Append('\n')
          .Append("📅 ").Append(d.Date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture))
          .Append(" · saat ").Append(d.TakenAtLocal.ToString("HH:mm", CultureInfo.InvariantCulture)).Append('\n');
        if (!string.IsNullOrEmpty(d.ScopeNote)) sb.Append("🔎 ").Append(WebUtility.HtmlEncode(d.ScopeNote)).Append('\n');
        sb.Append('\n');
        if (d.HeadcountTotal is int headcount)
            sb.Append("📌 Ştat: ").Append(headcount).Append(" · Vakant: ").Append(d.Vacancies).Append('\n');
        sb.Append("👥 İşçi sayı: ").Append(t.Employees).Append('\n')
          .Append("✅ İşdə: ").Append(t.Present).Append('\n')
          .Append("🏖 İstirahət: ").Append(t.Rest).Append('\n')
          .Append("📋 Məzuniyyət: ").Append(t.Vacation).Append('\n')
          .Append("❌ İşə gəlməyib: ").Append(t.Absent).Append('\n')
          .Append("🩺 İcazə / Xəstəlik: ").Append(t.Permission + t.Sick).Append('\n')
          .Append("🧳 Ezamiyyət: ").Append(t.Trip).Append('\n');
        if (t.Pending > 0) sb.Append("⏳ Növbəsi başlamayıb: ").Append(t.Pending).Append('\n');
        sb.Append("📍 Ərazi sayı: ").Append(d.Areas.Count);
        return sb.ToString();
    }

    // ───────────────────────────── sending ─────────────────────────────

    private DailyReportDelivery NewDelivery(DailyReportRecipient r, DailyReportData data, DeliveryKind kind) => new()
    {
        RecipientId = r.Id,
        ReportDate = data.Date,
        Kind = (int)kind,
        Status = (int)DeliveryStatus.Pending,
        NextAttemptUtc = NowUtc(),
        SnapshotJson = JsonSerializer.Serialize(data, Json),
        TokenHash = "pending",
        TokenExpiresUtc = NowUtc(),
    };

    /// <summary>
    /// One attempt: PDF from the frozen snapshot, a fresh link secret (only its hash is kept, so a link is
    /// minted per attempt and only the one that reached Telegram ever opens anything), and the send.
    /// The delivery's state is saved whatever happens.
    /// </summary>
    public async Task<SendOutcome> SendAsync(DailyReportDelivery d, DailyReportRecipient r, CancellationToken ct)
    {
        d.Attempts++;
        string? error;
        var permanent = false;
        int? retryAfter = null;
        try
        {
            if (r.TelegramChatId is not long chatId)
                throw new InvalidOperationException("Alıcı Telegram-a qoşulmayıb");
            var data = JsonSerializer.Deserialize<DailyReportData>(d.SnapshotJson, Json)
                       ?? throw new InvalidOperationException("Snapshot is empty");
            var html = DailyReportHtml.Render(data);
            var pdf = await _pdf.RenderAsync(html, ct);

            var secret = NewSecret(24);
            d.TokenHash = Hash(secret);
            d.TokenExpiresUtc = NowUtc().AddDays(Math.Max(1, _options.LinkValidDays));
            var url = PublicReportUrl($"{d.TenantId:N}.{d.Id:N}.{secret}");

            var res = await _telegram.SendDocumentAsync(chatId, pdf,
                $"davamiyyet-{data.Date:yyyy-MM-dd}.pdf", Caption(data), "🔗 HTML hesabatı aç", url, ct);
            if (res.Ok)
            {
                d.Status = (int)DeliveryStatus.Sent;
                d.SentAtUtc = NowUtc();
                d.LastError = null;
                await _db.SaveChangesAsync(ct);
                return new SendOutcome(d.Id, r.Id, r.Name, true, null);
            }
            error = res.Error;
            permanent = res.Permanent;
            retryAfter = res.RetryAfterSeconds;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            error = ex.GetType().Name + ": " + ex.Message;
            if (_telegram is TelegramClient tc) error = tc.Scrub(error);
        }

        d.LastError = error is { Length: > 500 } ? error[..500] : error;
        if (permanent || d.Attempts >= _options.MaxAttempts)
        {
            d.Status = (int)DeliveryStatus.Failed;
            _logger.LogError("DailyReport: delivery {Delivery} to recipient {Recipient} given up after {Attempts} attempt(s): {Error}",
                d.Id, r.Id, d.Attempts, d.LastError);
        }
        else
        {
            var wait = retryAfter is int s && s > 0
                ? TimeSpan.FromSeconds(Math.Min(s, 3600))
                : Backoff[Math.Min(d.Attempts - 1, Backoff.Length - 1)];
            d.NextAttemptUtc = NowUtc() + wait;
            _logger.LogWarning("DailyReport: delivery {Delivery} attempt {Attempts} failed, retrying in {Wait}: {Error}",
                d.Id, d.Attempts, wait, d.LastError);
        }
        await _db.SaveChangesAsync(ct);
        return new SendOutcome(d.Id, r.Id, r.Name, false, d.LastError);
    }

    /// <summary>
    /// The scheduled send for this company: once its time has come (and while still within
    /// <see cref="SendWindow"/>), one delivery per connected recipient for today — refused by a unique
    /// index if one already exists, so a restart or two ticks racing never send a report twice.
    /// Returns how many deliveries were created.
    /// </summary>
    public async Task<int> CreateScheduledAsync(CancellationToken ct)
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstAsync(t => t.Id == _tenant.TenantId, ct);
        if (tenant.DailyReportAtLocal is not TimeOnly at) return 0;

        var nowLocal = NowLocal();
        var today = DateOnly.FromDateTime(nowLocal);
        var due = today.ToDateTime(at);
        if (nowLocal < due || nowLocal >= due + SendWindow) return 0;

        var recipients = await _db.DailyReportRecipients
            .Where(r => r.IsActive && r.TelegramChatId != null)
            .ToListAsync(ct);
        if (recipients.Count == 0) return 0;
        var already = (await _db.DailyReportDeliveries
                .Where(d => d.ReportDate == today && d.Kind == (int)DeliveryKind.Scheduled)
                .Select(d => d.RecipientId)
                .ToListAsync(ct))
            .ToHashSet();

        var created = 0;
        var byScope = new Dictionary<string, DailyReportData>();
        foreach (var r in recipients.Where(r => !already.Contains(r.Id)))
        {
            var key = r.LocationIds ?? string.Empty;
            if (!byScope.TryGetValue(key, out var data))
                byScope[key] = data = await BuildDataAsync(r.LocationIds, ct);
            _db.DailyReportDeliveries.Add(NewDelivery(r, data, DeliveryKind.Scheduled));
            created++;
        }
        if (created == 0) return 0;
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another tick got there first (the unique index): nothing to do — that one will send.
            _db.ChangeTracker.Clear();
            return 0;
        }
        return created;
    }

    /// <summary>Sends whatever is waiting and due (new scheduled reports and retries). Returns how many went out.</summary>
    public async Task<int> ProcessDueAsync(CancellationToken ct)
    {
        var now = NowUtc();
        var due = await _db.DailyReportDeliveries
            .Where(d => d.Status == (int)DeliveryStatus.Pending && d.NextAttemptUtc <= now)
            .OrderBy(d => d.CreatedAtUtc)
            .Take(20)
            .ToListAsync(ct);
        if (due.Count == 0) return 0;

        var ids = due.Select(d => d.RecipientId).Distinct().ToList();
        var recipients = await _db.DailyReportRecipients.Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
        var sent = 0;
        foreach (var d in due)
        {
            if (!recipients.TryGetValue(d.RecipientId, out var r) || !r.IsActive || r.TelegramChatId is null)
            {
                d.Status = (int)DeliveryStatus.Failed;
                d.LastError = "Alıcı silinib və ya Telegram-dan ayrılıb";
                await _db.SaveChangesAsync(ct);
                continue;
            }
            if ((await SendAsync(d, r, ct)).Sent) sent++;
        }
        return sent;
    }

    /// <summary>«Hesabatı indi göndər»: a fresh report to every connected recipient (or just one), sent now. Not limited per day.</summary>
    public async Task<List<SendOutcome>> SendNowAsync(Guid? recipientId, CancellationToken ct)
    {
        var recipients = await _db.DailyReportRecipients
            .Where(r => r.IsActive && r.TelegramChatId != null && (recipientId == null || r.Id == recipientId))
            .ToListAsync(ct);
        var outcomes = new List<SendOutcome>();
        var byScope = new Dictionary<string, DailyReportData>();
        foreach (var r in recipients)
        {
            var key = r.LocationIds ?? string.Empty;
            if (!byScope.TryGetValue(key, out var data))
                byScope[key] = data = await BuildDataAsync(r.LocationIds, ct);
            var d = NewDelivery(r, data, DeliveryKind.Manual);
            _db.DailyReportDeliveries.Add(d);
            await _db.SaveChangesAsync(ct);
            outcomes.Add(await SendAsync(d, r, ct));
        }
        return outcomes;
    }

    // ───────────────────────────── the link ─────────────────────────────

    public string PublicReportUrl(string token)
        => _options.PublicBaseUrl.TrimEnd('/') + "/api/public/daily-report/" + token;

    /// <summary>
    /// Splits «&lt;company&gt;.&lt;delivery&gt;.&lt;secret&gt;». Only the shape is checked here; whether the
    /// company exists and the secret matches is decided by the page and <see cref="OpenAsync"/>.
    /// </summary>
    public static bool TryParseToken(string token, out Guid tenantId, out Guid deliveryId, out string secret)
    {
        tenantId = deliveryId = Guid.Empty;
        secret = string.Empty;
        var parts = token.Split('.');
        if (parts.Length != 3 || parts[2].Length < 16 || parts[2].Length > 64) return false;
        if (!Guid.TryParseExact(parts[0], "N", out tenantId) || !Guid.TryParseExact(parts[1], "N", out deliveryId)) return false;
        secret = parts[2];
        return true;
    }

    public enum OpenResult { Ok, NotFound, Expired }

    /// <summary>The page behind a link: only for a token minted by this company, unexpired, of a recipient still active.</summary>
    public async Task<(OpenResult Result, string? Html)> OpenAsync(string token, CancellationToken ct)
    {
        if (!TryParseToken(token, out var tenantId, out var id, out var secret) || tenantId != _tenant.TenantId)
            return (OpenResult.NotFound, null);

        var d = await _db.DailyReportDeliveries.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null || d.Status != (int)DeliveryStatus.Sent || !FixedEquals(d.TokenHash, Hash(secret)))
            return (OpenResult.NotFound, null);
        var active = await _db.DailyReportRecipients.AnyAsync(r => r.Id == d.RecipientId && r.IsActive, ct);
        if (!active) return (OpenResult.NotFound, null);
        if (d.TokenExpiresUtc <= NowUtc()) return (OpenResult.Expired, null);

        var data = JsonSerializer.Deserialize<DailyReportData>(d.SnapshotJson, Json);
        return data is null ? (OpenResult.NotFound, null) : (OpenResult.Ok, DailyReportHtml.Render(data));
    }

    // ───────────────────────────── connecting a recipient ─────────────────────────────

    /// <summary>
    /// A fresh one-time «connect» link for a recipient (valid 48 h; any older one stops working). The
    /// start payload names the company in clear and carries a random secret whose hash only is stored.
    /// </summary>
    public string NewConnectLink(DailyReportRecipient r)
    {
        var secret = NewSecret(16);
        r.LinkCodeHash = Hash(secret);
        r.LinkCodeExpiresUtc = NowUtc() + ConnectLinkLife;
        return $"https://t.me/{_options.TelegramBotUsername.TrimStart('@')}?start={_tenant.TenantId:N}_{secret}";
    }

    /// <summary>Binds the chat that pressed «Start» to the recipient whose code it carried. Returns the bot's reply.</summary>
    public async Task<string> BindAsync(string secret, long chatId, CancellationToken ct)
    {
        var hash = Hash(secret);
        var now = NowUtc();
        var r = await _db.DailyReportRecipients
            .FirstOrDefaultAsync(x => x.LinkCodeHash == hash && x.IsActive, ct);
        if (r is null || r.LinkCodeExpiresUtc is null || r.LinkCodeExpiresUtc <= now)
            return "Bu qoşulma linki etibarsızdır və ya müddəti bitib. Şirkətin administratorundan yeni link istəyin.";
        r.TelegramChatId = chatId;
        r.LinkedAtUtc = now;
        r.LinkCodeHash = null;
        r.LinkCodeExpiresUtc = null;
        await _db.SaveChangesAsync(ct);
        var company = await _db.Tenants.Where(t => t.Id == _tenant.TenantId).Select(t => t.Name).FirstAsync(ct);
        return $"✅ Qoşuldunuz. «{WebUtility.HtmlEncode(company)}» üzrə gündəlik davamiyyət hesabatı bu çata göndəriləcək.";
    }

    // ───────────────────────────── helpers ─────────────────────────────

    public static string NewSecret(int bytes)
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string secret)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();

    private static bool FixedEquals(string a, string b)
        => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));
}
