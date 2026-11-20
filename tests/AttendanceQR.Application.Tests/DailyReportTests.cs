using System.Text.Json;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Api.DailyReport;
using AttendanceQR.Application.Common;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Application.Reporting.DailyReport;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The daily leadership report on Telegram. What these pin:
/// the report counts a day exactly as the board does (one fixture for both copies of the rule); its columns
/// always add up to the head count; a flexible-schedule day with no scan is never «İşə gəlməyib»; a
/// scheduled report goes once per recipient per day; a failed send is retried and then given up; the link
/// opens only its own company's report, only with the right secret, only until it expires; the bot token
/// never survives into an error; and a «connect» code binds once.
/// </summary>
public class DailyReportTests
{
    private static readonly DateOnly Day = new(2026, 11, 20);

    // ───────────────────────────── one rule, two copies ─────────────────────────────

    [Fact]
    public void Server_buckets_agree_with_the_boards_fixture()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AttendanceQR.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var path = Path.Combine(dir!.FullName, "frontend", "src", "pages", "admin", "todayBuckets.fixture.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));

        var cases = doc.RootElement.GetProperty("cases").EnumerateArray().ToList();
        Assert.True(cases.Count >= 15, "the fixture lost its cases");
        var wrong = new List<string>();
        foreach (var c in cases)
        {
            var status = c.GetProperty("status").GetString()!;
            var leave = c.TryGetProperty("leaveType", out var l) ? l.GetString() : null;
            var fieldOut = c.TryGetProperty("fieldCheckedOut", out var f) && f.GetBoolean();
            var flexible = c.TryGetProperty("isFlexible", out var x) && x.GetBoolean();
            var expected = c.GetProperty("bucket").GetString();
            var actual = TodayBuckets.BucketOf(status, leave, fieldOut ? DateTime.UtcNow : null, flexible);
            if (actual != expected) wrong.Add($"{status}/{leave}/flex={flexible}/out={fieldOut}: {actual} ≠ {expected}");
        }
        Assert.Empty(wrong);
    }

    // ───────────────────────────── the figures ─────────────────────────────

    private static DayAttendanceRow Row(Guid loc, string status, string? leave = null, bool flexible = false,
        DateTime? fieldIn = null, DateTime? fieldOut = null) =>
        new(Guid.NewGuid(), "İşçi", loc, "Filial", status, null, null, null, false, null, "None",
            LeaveType: leave, FieldCheckInAtUtc: fieldIn, FieldCheckOutAtUtc: fieldOut, IsFlexible: flexible);

    [Fact]
    public void Every_person_is_in_exactly_one_column_and_a_flexible_day_without_a_scan_is_not_absence()
    {
        var a = Guid.NewGuid();
        var rows = new[]
        {
            Row(a, "OnTime"), Row(a, "Late"), Row(a, "Incomplete"), Row(a, "Field", fieldIn: DateTime.UtcNow),
            Row(a, "Absent"),
            Row(a, "DayOff", flexible: true),            // elastik, no scan
            Row(a, "Pending"), Row(a, "Onboarding"),
            Row(a, "DayOff"), Row(a, "DayOff", "Rest"),
            Row(a, "OnLeave", "Vacation"), Row(a, "OnLeave", "Sick"), Row(a, "OnLeave", "BusinessTrip"),
            Row(a, "OnLeave", "Unpaid"), Row(a, "Permission", "Permission"),
        };
        var d = DailyReportBuilder.Build("Şirkət", Day, Day.ToDateTime(new TimeOnly(18, 0)), rows,
            [new DailyReportLocation(a, "Mərkəz", null, null)], null);

        var t = d.Totals;
        Assert.Equal(15, t.Employees);
        Assert.True(t.IsConsistent);
        Assert.Equal(4, t.Present);       // two finished, one still in, one on a site
        Assert.Equal(1, t.Absent);        // NOT the flexible one
        Assert.Equal(1, t.FlexNoScan);
        Assert.Equal(1, t.Pending);
        Assert.Equal(1, t.Onboarding);
        Assert.Equal(2, t.Rest);          // the roster's day off and a granted rest day
        Assert.Equal(1, t.Vacation);
        Assert.Equal(1, t.Sick);
        Assert.Equal(1, t.Trip);
        Assert.Equal(1, t.Unpaid);
        Assert.Equal(1, t.Permission);
    }

    [Fact]
    public void Vacancy_is_shtat_minus_people_and_an_empty_staffed_branch_still_shows()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        var rows = Enumerable.Range(0, 11).Select(_ => Row(a, "OnTime"))
            .Concat(Enumerable.Range(0, 3).Select(_ => Row(c, "Absent")))
            .ToList();
        var locations = new[]
        {
            new DailyReportLocation(a, "Park", 15, "Şəhər parkları"),
            new DailyReportLocation(b, "Yeni sahə", 5, "Şəhər parkları"),   // staffed, nobody hired yet
            new DailyReportLocation(c, "Anbar", null, null),                // no ştat set
        };
        var d = DailyReportBuilder.Build("Şirkət", Day, Day.ToDateTime(new TimeOnly(18, 0)), rows, locations, null);

        Assert.Equal(3, d.Areas.Count);
        Assert.Equal(20, d.HeadcountTotal);
        Assert.Equal(9, d.Vacancies);       // (15 − 11) + (5 − 0); the branch without a ştat adds nothing
        Assert.Equal(5, d.Areas.Single(x => x.Name == "Yeni sahə").Vacancies);
        Assert.Equal(14, d.Totals.Employees);
        var parks = Assert.Single(d.Structures);
        Assert.Equal(11, parks.Employees);
    }

    [Fact]
    public void A_recipient_with_branches_sees_only_those_branches()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        var rows = new[] { Row(a, "OnTime"), Row(a, "Absent"), Row(b, "OnTime"), Row(b, "OnTime"), Row(b, "OnTime") };
        var locations = new[] { new DailyReportLocation(a, "A", null, null), new DailyReportLocation(b, "B", 10, null) };

        var d = DailyReportBuilder.Build("Şirkət", Day, Day.ToDateTime(new TimeOnly(18, 0)), rows, locations,
            new HashSet<Guid> { a });

        Assert.Equal(2, d.Totals.Employees);
        Assert.Single(d.Areas);
        Assert.Null(d.HeadcountTotal);      // B's ştat is not this recipient's business
        Assert.NotNull(d.ScopeNote);
    }

    [Fact]
    public void The_page_escapes_names_and_shows_optional_columns_only_when_used()
    {
        var a = Guid.NewGuid();
        var data = DailyReportBuilder.Build("<script>alert(1)</script> MMC", Day, Day.ToDateTime(new TimeOnly(18, 0)),
            [Row(a, "OnTime"), Row(a, "Absent")], [new DailyReportLocation(a, "Bağ & <Park>", 4, null)], null);

        var html = DailyReportHtml.Render(data);
        Assert.DoesNotContain("<script>alert", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("Bağ &amp; &lt;Park&gt;", html);
        Assert.Contains("lang=\"az\"", html);
        Assert.Contains("Ştat", html);
        Assert.DoesNotContain("Ödənişsiz", html);

        var withUnpaid = DailyReportBuilder.Build("MMC", Day, Day.ToDateTime(new TimeOnly(18, 0)),
            [Row(a, "OnLeave", "Unpaid")], [new DailyReportLocation(a, "Park", null, null)], null);
        Assert.Contains("Ödənişsiz", DailyReportHtml.Render(withUnpaid));
    }

    // ───────────────────────────── sending ─────────────────────────────

    [Fact]
    public async Task A_scheduled_report_goes_once_per_recipient_per_day_and_only_in_its_window()
    {
        using var w = new World();
        w.SetTime(new TimeOnly(19, 15));
        var linked = w.AddRecipient("Direktor", chatId: 111);
        w.AddRecipient("Hələ qoşulmayıb", chatId: null);
        w.AddRecipient("Çıxarılıb", chatId: 222, active: false);

        w.Clock.Local = Day.ToDateTime(new TimeOnly(19, 0));
        Assert.Equal(0, await w.Service().CreateScheduledAsync(default));      // not yet

        w.Clock.Local = Day.ToDateTime(new TimeOnly(19, 30));
        Assert.Equal(1, await w.Service().CreateScheduledAsync(default));
        Assert.Equal(0, await w.Service().CreateScheduledAsync(default));      // the next tick, a restart…
        Assert.Equal(1, await w.Service().ProcessDueAsync(default));

        var call = Assert.Single(w.Telegram.Documents);
        Assert.Equal(111, call.ChatId);
        Assert.Equal("davamiyyet-2026-11-20.pdf", call.FileName);
        Assert.Contains("İşçi sayı", call.Caption);
        var delivery = Assert.Single(w.Db.DailyReportDeliveries.AsNoTracking());
        Assert.Equal(linked.Id, delivery.RecipientId);
        Assert.Equal((int)DeliveryStatus.Sent, delivery.Status);

        w.Clock.Local = Day.ToDateTime(new TimeOnly(23, 0));                   // window long gone
        Assert.Equal(0, await w.Service().CreateScheduledAsync(default));
    }

    [Fact]
    public async Task Nothing_is_scheduled_while_the_company_has_not_switched_it_on()
    {
        using var w = new World();
        w.AddRecipient("Direktor", chatId: 111);
        w.Clock.Local = Day.ToDateTime(new TimeOnly(18, 30));
        Assert.Equal(0, await w.Service().CreateScheduledAsync(default));
    }

    [Fact]
    public async Task A_failed_send_is_retried_later_and_then_succeeds()
    {
        using var w = new World();
        w.AddRecipient("Direktor", chatId: 111);
        w.Clock.Local = Day.ToDateTime(new TimeOnly(19, 30));
        w.Telegram.Results.Enqueue(new TelegramResult(false, "502: Bad Gateway"));

        var outcome = Assert.Single(await w.Service().SendNowAsync(null, default));
        Assert.False(outcome.Sent);
        var d = w.Db.DailyReportDeliveries.AsNoTracking().Single();
        Assert.Equal((int)DeliveryStatus.Pending, d.Status);
        Assert.Equal(1, d.Attempts);
        Assert.Equal(w.Clock.GetUtcNow().UtcDateTime.AddMinutes(1), d.NextAttemptUtc);

        Assert.Equal(0, await w.Service().ProcessDueAsync(default));          // not due yet
        w.Clock.Local = w.Clock.Local.AddMinutes(1);
        Assert.Equal(1, await w.Service().ProcessDueAsync(default));
        Assert.Equal(2, w.Telegram.Documents.Count);
        d = w.Db.DailyReportDeliveries.AsNoTracking().Single();
        Assert.Equal((int)DeliveryStatus.Sent, d.Status);
        Assert.Null(d.LastError);
    }

    [Fact]
    public async Task Telegrams_retry_after_is_respected()
    {
        using var w = new World();
        w.AddRecipient("Direktor", chatId: 111);
        w.Clock.Local = Day.ToDateTime(new TimeOnly(19, 30));
        w.Telegram.Results.Enqueue(new TelegramResult(false, "429: Too Many Requests", RetryAfterSeconds: 30));
        await w.Service().SendNowAsync(null, default);
        var d = w.Db.DailyReportDeliveries.AsNoTracking().Single();
        Assert.Equal(w.Clock.GetUtcNow().UtcDateTime.AddSeconds(30), d.NextAttemptUtc);
    }

    [Fact]
    public async Task A_chat_that_is_gone_fails_at_once_and_a_flaky_one_gives_up_after_the_limit()
    {
        using var w = new World(maxAttempts: 2);
        w.AddRecipient("Botu bloklayıb", chatId: 111);
        w.Clock.Local = Day.ToDateTime(new TimeOnly(19, 30));
        w.Telegram.Results.Enqueue(new TelegramResult(false, "403: Forbidden: bot was blocked by the user", Permanent: true));
        await w.Service().SendNowAsync(null, default);
        Assert.Equal((int)DeliveryStatus.Failed, w.Db.DailyReportDeliveries.AsNoTracking().Single().Status);

        using var v = new World(maxAttempts: 2);
        v.AddRecipient("Zəif internet", chatId: 222);
        v.Clock.Local = Day.ToDateTime(new TimeOnly(19, 30));
        v.Telegram.Results.Enqueue(new TelegramResult(false, "timeout"));
        v.Telegram.Results.Enqueue(new TelegramResult(false, "timeout"));
        await v.Service().SendNowAsync(null, default);
        v.Clock.Local = v.Clock.Local.AddMinutes(5);
        await v.Service().ProcessDueAsync(default);
        var d = v.Db.DailyReportDeliveries.AsNoTracking().Single();
        Assert.Equal((int)DeliveryStatus.Failed, d.Status);
        Assert.Equal(2, d.Attempts);
    }

    // ───────────────────────────── the link ─────────────────────────────

    [Fact]
    public async Task The_link_opens_with_its_secret_only_until_it_expires_and_only_while_the_recipient_stays()
    {
        using var w = new World();
        var r = w.AddRecipient("Direktor", chatId: 111);
        w.Clock.Local = Day.ToDateTime(new TimeOnly(19, 30));
        await w.Service().SendNowAsync(null, default);

        var url = Assert.Single(w.Telegram.Documents).ButtonUrl;
        const string prefix = "https://api-test.example/api/public/daily-report/";
        Assert.StartsWith(prefix, url);
        var token = url[prefix.Length..];
        Assert.True(DailyReportService.TryParseToken(token, out var tenantId, out _, out var secret));
        Assert.Equal(w.TenantId, tenantId);

        // Only the hash is kept.
        var stored = w.Db.DailyReportDeliveries.AsNoTracking().Single();
        Assert.DoesNotContain(secret, stored.TokenHash);
        Assert.Equal(DailyReportService.Hash(secret), stored.TokenHash);

        var (ok, html) = await w.Service().OpenAsync(token, default);
        Assert.Equal(DailyReportService.OpenResult.Ok, ok);
        Assert.Contains("Test MMC", html);

        var tampered = token[..^2] + (token[^2..] == "AA" ? "BB" : "AA");
        Assert.Equal(DailyReportService.OpenResult.NotFound, (await w.Service().OpenAsync(tampered, default)).Result);
        var otherCompany = $"{Guid.NewGuid():N}{token[32..]}";
        Assert.Equal(DailyReportService.OpenResult.NotFound, (await w.Service().OpenAsync(otherCompany, default)).Result);

        w.Clock.Local = w.Clock.Local.AddDays(8);
        Assert.Equal(DailyReportService.OpenResult.Expired, (await w.Service().OpenAsync(token, default)).Result);

        w.Clock.Local = Day.ToDateTime(new TimeOnly(20, 0));
        var tracked = w.Db.DailyReportRecipients.Single(x => x.Id == r.Id);
        tracked.IsActive = false;
        await w.Db.SaveChangesAsync();
        Assert.Equal(DailyReportService.OpenResult.NotFound, (await w.Service().OpenAsync(token, default)).Result);
    }

    [Fact]
    public async Task The_public_page_resolves_the_company_from_the_link_and_refuses_an_inactive_or_foreign_one()
    {
        using var w = new World();
        w.AddRecipient("Direktor", chatId: 111);
        w.Clock.Local = Day.ToDateTime(new TimeOnly(19, 30));
        await w.Service().SendNowAsync(null, default);
        var token = w.Telegram.Documents.Single().ButtonUrl.Split('/').Last();

        // Opened on the API host: nothing resolved the company yet.
        var page = await w.PublicPage(new TenantContext()).Open(token);
        var content = Assert.IsType<ContentResult>(page);
        Assert.Null(content.StatusCode);
        Assert.Contains("Test MMC", content.Content);

        // Resolved to a different company (some other host): never this company's report.
        var foreign = new TenantContext();
        foreign.Resolve(Guid.NewGuid());
        var refused = Assert.IsType<ContentResult>(await w.PublicPage(foreign).Open(token));
        Assert.Equal(StatusCodes.Status404NotFound, refused.StatusCode);

        // A switched-off company's links stop opening.
        var t = w.Db.Tenants.Single();
        t.IsActive = false;
        await w.Db.SaveChangesAsync();
        var off = Assert.IsType<ContentResult>(await w.PublicPage(new TenantContext()).Open(token));
        Assert.Equal(StatusCodes.Status404NotFound, off.StatusCode);

        var junk = Assert.IsType<ContentResult>(await w.PublicPage(new TenantContext()).Open("not-a-token"));
        Assert.Equal(StatusCodes.Status404NotFound, junk.StatusCode);
    }

    // ───────────────────────────── secrets ─────────────────────────────

    [Fact]
    public void The_bot_token_never_survives_into_an_error()
    {
        var options = new DailyReportOptions { TelegramBotToken = "123456:AAHsecretPART" };
        var client = new TelegramClient(new NoHttp(), options);
        var scrubbed = client.Scrub("HttpRequestException: https://api.telegram.org/bot123456:AAHsecretPART/sendDocument failed");
        Assert.DoesNotContain("AAHsecretPART", scrubbed);
        Assert.Contains("***", scrubbed);
    }

    [Fact]
    public async Task A_connect_code_binds_one_chat_once_and_runs_out()
    {
        using var w = new World();
        var r = w.AddRecipient("Direktor", chatId: null);
        var tracked = w.Db.DailyReportRecipients.Single(x => x.Id == r.Id);
        var service = w.Service();
        var link = service.NewConnectLink(tracked);
        await w.Db.SaveChangesAsync();

        Assert.StartsWith($"https://t.me/qrlog_test_bot?start={w.TenantId:N}_", link);
        var secret = link[(link.IndexOf('_', link.IndexOf("start=", StringComparison.Ordinal)) + 1)..];
        Assert.True(link.Length - link.IndexOf("start=", StringComparison.Ordinal) - 6 <= 64, "Telegram allows 64 chars in a start payload");

        var reply = await w.Service().BindAsync(secret, 555, default);
        Assert.StartsWith("✅", reply);
        var bound = w.Db.DailyReportRecipients.AsNoTracking().Single(x => x.Id == r.Id);
        Assert.Equal(555, bound.TelegramChatId);
        Assert.Null(bound.LinkCodeHash);

        Assert.DoesNotContain("✅", await w.Service().BindAsync(secret, 666, default));     // used up
        Assert.Equal(555, w.Db.DailyReportRecipients.AsNoTracking().Single(x => x.Id == r.Id).TelegramChatId);

        var again = w.Db.DailyReportRecipients.Single(x => x.Id == r.Id);
        var link2 = w.Service().NewConnectLink(again);
        await w.Db.SaveChangesAsync();
        var secret2 = link2[(link2.IndexOf('_', link2.IndexOf("start=", StringComparison.Ordinal)) + 1)..];
        w.Clock.Local = w.Clock.Local.AddHours(49);
        Assert.DoesNotContain("✅", await w.Service().BindAsync(secret2, 777, default));    // ran out
    }

    // ───────────────────────────── harness ─────────────────────────────

    private sealed class World : IDisposable
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public Guid LocationId { get; } = Guid.NewGuid();
        public AppDbContext Db { get; }
        public BakuClock Clock { get; } = new();
        public FakeTelegram Telegram { get; } = new();
        private readonly string _dbName = $"daily-report-{Guid.NewGuid()}";
        private readonly TenantContext _tenant = new();
        private readonly DailyReportOptions _options;
        private readonly List<IDisposable> _extra = [];

        public World(int maxAttempts = 6)
        {
            _options = new DailyReportOptions
            {
                TelegramBotToken = "test-token",
                TelegramBotUsername = "qrlog_test_bot",
                PublicBaseUrl = "https://api-test.example/",
                MaxAttempts = maxAttempts,
            };
            _tenant.Resolve(TenantId);
            Db = NewDb(_tenant);
            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "Test MMC", Slug = "test", DisplayName = "Test MMC", IsActive = true });
            Db.Locations.Add(new Location { Id = LocationId, Name = "Mərkəz", RadiusMeters = 100, Headcount = 3, IsActive = true });
            Db.SaveChanges();
            Clock.Local = Day.ToDateTime(new TimeOnly(12, 0));
        }

        private AppDbContext NewDb(ITenantContext tenant)
        {
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options, tenant);
            return db;
        }

        public void SetTime(TimeOnly at)
        {
            var t = Db.Tenants.Single();
            t.DailyReportAtLocal = at;
            Db.SaveChanges();
        }

        public DailyReportRecipient AddRecipient(string name, long? chatId, bool active = true)
        {
            var r = new DailyReportRecipient { Name = name, TelegramChatId = chatId, IsActive = active };
            Db.DailyReportRecipients.Add(r);
            Db.SaveChanges();
            Db.ChangeTracker.Clear();
            return r;
        }

        public DailyReportService Service() => Service(Db, _tenant);

        private DailyReportService Service(AppDbContext db, ITenantContext tenant) =>
            new(db, FakeBoard.For(LocationId), Telegram, new FakePdf(), _options, new AppOptions { TimeZone = "Asia/Baku" },
                tenant, NullLogger<DailyReportService>.Instance, Clock);

        public PublicDailyReportController PublicPage(TenantContext tenant)
        {
            var db = NewDb(tenant);
            _extra.Add(db);
            return new PublicDailyReportController(Service(db, tenant), db, tenant)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            };
        }

        public void Dispose()
        {
            foreach (var d in _extra) d.Dispose();
            Db.Dispose();
        }
    }

    /// <summary>A clock set in Baku wall time (UTC+4, no DST).</summary>
    private sealed class BakuClock : TimeProvider
    {
        public DateTime Local { get; set; }
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Local.AddHours(-4), DateTimeKind.Utc), TimeSpan.Zero);
    }

    /// <summary>The board, faked: only the one call the report makes is answered; anything else is a test bug.</summary>
    public class FakeBoard : System.Reflection.DispatchProxy
    {
        public IReadOnlyList<DayAttendanceRow> Rows { get; set; } = [];

        public static IReportQueryService For(Guid locationId)
        {
            var proxy = Create<IReportQueryService, FakeBoard>();
            ((FakeBoard)(object)proxy).Rows = [Row(locationId, "OnTime"), Row(locationId, "Absent")];
            return proxy;
        }

        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IReportQueryService.GetBoardForCompanyAsync)
                ? Task.FromResult(Rows)
                : throw new NotSupportedException(targetMethod?.Name);
    }

    private sealed class FakePdf : IPdfRenderer
    {
        public Task<byte[]> RenderAsync(string html, CancellationToken ct) => Task.FromResult("%PDF-1.7 test"u8.ToArray());
    }

    public sealed record SentDocument(long ChatId, string FileName, string Caption, string ButtonUrl);

    private sealed class FakeTelegram : ITelegramClient
    {
        public Queue<TelegramResult> Results { get; } = new();
        public List<SentDocument> Documents { get; } = [];

        public Task<TelegramResult> SendDocumentAsync(long chatId, byte[] file, string fileName, string captionHtml,
            string buttonText, string buttonUrl, CancellationToken ct)
        {
            Documents.Add(new SentDocument(chatId, fileName, captionHtml, buttonUrl));
            return Task.FromResult(Results.Count > 0 ? Results.Dequeue() : new TelegramResult(true));
        }

        public Task<TelegramResult> SendMessageAsync(long chatId, string textHtml, CancellationToken ct) => Task.FromResult(new TelegramResult(true));

        public Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, int timeoutSeconds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TelegramUpdate>>([]);
    }

    private sealed class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new NotSupportedException();
    }
}
