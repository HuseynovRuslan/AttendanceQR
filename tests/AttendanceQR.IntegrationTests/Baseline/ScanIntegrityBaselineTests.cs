using System.Net;
using AttendanceQR.Domain.Enums;
using AttendanceQR.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace AttendanceQR.IntegrationTests.Baseline;

/// <summary>
/// What a double tap, a retry storm or a re-sent offline queue does to the attendance ledger — against the real
/// unique index (<c>IX_AttendanceRecords_OneOpenPerDay</c>) and real concurrency. The InMemory provider enforces
/// no index, so none of this was tested before. Pins today's guarantee: however many identical scans arrive at
/// once, the ledger keeps exactly one record. (The response codes are reported, not asserted beyond "one
/// success": a race on a NEW device's first scan still answers 500 — BIZ-07, a later package.)
/// </summary>
public class ScanIntegrityBaselineTests : IClassFixture<ScanIntegrityBaselineTests.World>
{
    public sealed class World : IAsyncLifetime
    {
        public const string Pin = "4821";
        public string Cs = "";
        public QrLogApiFactory Factory = null!;
        public HttpClient Client = null!;
        public Guid T = Seed.OriginalTenantId, Location;
        public string QrToken = "";

        public async Task InitializeAsync()
        {
            if (!PostgresAvailability.CanRun(out _)) return;
            Cs = await PostgresServer.CreateDatabaseAsync();
            Location = await Seed.LocationAsync(Cs, T);
            await Seed.EmployeeAsync(Cs, T, Location, "admin@scan.test", Pin, EmployeeRole.Admin);

            Factory = new QrLogApiFactory(Cs);
            Client = Factory.CreateClient();
            var admin = await ApiCalls.LoginAsync(Client, Seed.OriginalTenantSlug, "admin@scan.test", Pin);
            var (status, qr) = await ApiCalls.SendAsync(Client, HttpMethod.Post,
                $"/api/admin/locations/{Location}/static-qr?validityDays=30", admin, body: new { });
            Assert.Equal(HttpStatusCode.OK, status);
            QrToken = qr.GetProperty("token").GetString()!;
        }

        public async Task DisposeAsync()
        {
            if (Factory is not null) await Factory.DisposeAsync();
        }

        /// <summary>A fresh employee with a phone that is already bound, signed in.</summary>
        public async Task<(Guid Id, string Token, string Fingerprint)> NewScannerAsync()
        {
            var email = $"worker-{Guid.NewGuid():N}@scan.test";
            var id = await Seed.EmployeeAsync(Cs, T, Location, email, Pin, allDayHours: true);
            var fingerprint = "it-device-" + Guid.NewGuid().ToString("N");
            await Seed.DeviceAsync(Cs, T, id, fingerprint);
            var token = await ApiCalls.LoginAsync(Client, Seed.OriginalTenantSlug, email, Pin);
            return (id, token, fingerprint);
        }

        public object ScanBody(string fingerprint, Guid? clientScanId = null) => new
        {
            qrToken = QrToken, deviceFingerprint = fingerprint, latitude = 40.4093, longitude = 49.8671,
            clientScanId = clientScanId ?? Guid.NewGuid(),
        };
    }

    private readonly World _w;
    private readonly ITestOutputHelper _out;
    public ScanIntegrityBaselineTests(World world, ITestOutputHelper output) { _w = world; _out = output; }

    private async Task<List<(HttpStatusCode Status, string Outcome)>> FireAsync(string token, Func<object> body, int n = 12)
    {
        using var start = new SemaphoreSlim(0, n);
        var tasks = Enumerable.Range(0, n).Select(async _ =>
        {
            await start.WaitAsync();
            var (status, json) = await ApiCalls.SendAsync(_w.Client, HttpMethod.Post, "/api/attendance/scan", token, body: body());
            var outcome = json.ValueKind == System.Text.Json.JsonValueKind.Object
                ? (json.TryGetProperty("action", out var a) ? a.GetString() : json.TryGetProperty("error", out var e) ? e.GetString() : "") ?? ""
                : "";
            return ((HttpStatusCode, string))(status, outcome);
        }).ToList();
        start.Release(n);
        var results = (await Task.WhenAll(tasks)).ToList();
        _out.WriteLine(string.Join(", ", results.GroupBy(r => $"{(int)r.Item1}:{r.Item2}").Select(g => $"{g.Key}×{g.Count()}")));
        return results;
    }

    [PostgresFact]
    public async Task Twelve_simultaneous_check_ins_leave_exactly_one_record()
    {
        var (id, token, fp) = await _w.NewScannerAsync();

        var results = await FireAsync(token, () => _w.ScanBody(fp));

        var (total, open) = await Seed.AttendanceCountsAsync(_w.Cs, _w.T, id);
        Assert.Equal(1, total);
        Assert.Equal(1, open);
        Assert.Equal(1, results.Count(r => r.Status == HttpStatusCode.OK && r.Outcome == "CheckIn"));
    }

    [PostgresFact]
    public async Task Twelve_simultaneous_check_outs_close_the_one_record_and_open_no_new_one()
    {
        var (id, token, fp) = await _w.NewScannerAsync();
        var (first, _) = await ApiCalls.SendAsync(_w.Client, HttpMethod.Post, "/api/attendance/scan", token, body: _w.ScanBody(fp));
        Assert.Equal(HttpStatusCode.OK, first);
        await Seed.BackdateOpenCheckInAsync(_w.Cs, id, TimeSpan.FromHours(3));

        var results = await FireAsync(token, () => _w.ScanBody(fp));

        var (total, open) = await Seed.AttendanceCountsAsync(_w.Cs, _w.T, id);
        Assert.Equal(1, total);
        Assert.Equal(0, open);
        Assert.Contains(results, r => r.Status == HttpStatusCode.OK && r.Outcome == "CheckOut");
        Assert.DoesNotContain(results, r => r.Outcome == "CheckIn");
    }

    [PostgresFact]
    public async Task A_scan_re_sent_with_the_same_client_id_is_recorded_once()
    {
        var (id, token, fp) = await _w.NewScannerAsync();
        var clientScanId = Guid.NewGuid();

        var (s1, b1) = await ApiCalls.SendAsync(_w.Client, HttpMethod.Post, "/api/attendance/scan", token, body: _w.ScanBody(fp, clientScanId));
        var (s2, b2) = await ApiCalls.SendAsync(_w.Client, HttpMethod.Post, "/api/attendance/scan", token, body: _w.ScanBody(fp, clientScanId));

        Assert.Equal(HttpStatusCode.OK, s1);
        Assert.Equal("CheckIn", b1.GetProperty("action").GetString());
        Assert.Equal(HttpStatusCode.OK, s2);
        Assert.Equal("AlreadyRecorded", b2.GetProperty("action").GetString());
        Assert.Equal((1, 1), await Seed.AttendanceCountsAsync(_w.Cs, _w.T, id));
    }
}
