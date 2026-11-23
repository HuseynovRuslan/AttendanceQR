using System.Net;
using System.Text;
using System.Text.Json;
using AttendanceQR.Domain.Enums;
using AttendanceQR.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.IntegrationTests.Baseline;

/// <summary>
/// Two companies on one database, over HTTP: company A's admin tries every way to read or change company B.
/// These all hold today (they were verified by hand in the production-readiness audit); this makes them a
/// regression suite, so a later change cannot quietly reopen one.
/// </summary>
public class TenantIsolationBaselineTests : IClassFixture<TenantIsolationBaselineTests.World>
{
    public sealed class World : IAsyncLifetime
    {
        public const string Pin = "4821";
        public string Cs = "";
        public QrLogApiFactory Factory = null!;
        public HttpClient Client = null!;
        public Guid A = Seed.OriginalTenantId, B, ALocation, BLocation, BEmployee, BAdmin;
        public string AToken = "", BToken = "";

        public async Task InitializeAsync()
        {
            if (!PostgresAvailability.CanRun(out _)) return;
            Cs = await PostgresServer.CreateDatabaseAsync();
            B = await Seed.TenantAsync(Cs, "betaco");
            ALocation = await Seed.LocationAsync(Cs, A, "A ofis");
            BLocation = await Seed.LocationAsync(Cs, B, "B ofis");
            await Seed.EmployeeAsync(Cs, A, ALocation, "admin@a.test", Pin, EmployeeRole.Admin);
            BAdmin = await Seed.EmployeeAsync(Cs, B, BLocation, "admin@b.test", Pin, EmployeeRole.Admin);
            BEmployee = await Seed.EmployeeAsync(Cs, B, BLocation, "worker@b.test", Pin);

            await using (var db = Seed.Context(Cs, B))
            {
                db.AttendanceRecords.Add(new AttendanceQR.Domain.Entities.AttendanceRecord
                {
                    EmployeeId = BEmployee, LocationId = BLocation, AttendanceDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)),
                    CheckInAtUtc = DateTime.UtcNow.AddDays(-2).Date.AddHours(5), CheckOutAtUtc = DateTime.UtcNow.AddDays(-2).Date.AddHours(14),
                });
                await db.SaveChangesAsync();
            }

            Factory = new QrLogApiFactory(Cs);
            Client = Factory.CreateClient();
            AToken = await ApiCalls.LoginAsync(Client, Seed.OriginalTenantSlug, "admin@a.test", Pin);
            BToken = await ApiCalls.LoginAsync(Client, "betaco", "admin@b.test", Pin);
        }

        public async Task DisposeAsync()
        {
            if (Factory is not null) await Factory.DisposeAsync();
        }

        public string AOrigin => ApiCalls.OriginFor(Seed.OriginalTenantSlug);
        public string BOrigin => ApiCalls.OriginFor("betaco");
    }

    private readonly World _w;
    public TenantIsolationBaselineTests(World world) => _w = world;

    [PostgresFact]
    public async Task A_request_that_names_no_company_is_refused_not_defaulted()
    {
        var (status, body) = await ApiCalls.SendAsync(_w.Client, HttpMethod.Post, "/api/auth/login",
            body: new { email = "admin@a.test", password = World.Pin });
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("TenantUnresolved", body.GetProperty("error").GetString());
    }

    [PostgresFact]
    public async Task One_company_s_credentials_do_not_sign_in_on_another_company_s_subdomain()
    {
        var (status, _) = await ApiCalls.SendAsync(_w.Client, HttpMethod.Post, "/api/auth/login", origin: _w.BOrigin,
            body: new { email = "admin@a.test", password = World.Pin });
        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    [PostgresFact]
    public async Task Company_A_cannot_list_or_open_company_B_employees_whatever_the_Origin()
    {
        var (s1, list) = await ApiCalls.GetAsync(_w.Client, "/api/admin/employees?pageSize=200", _w.AToken);
        Assert.Equal(HttpStatusCode.OK, s1);
        Assert.DoesNotContain(Items(list), e => e.GetProperty("id").GetGuid() == _w.BEmployee);

        var (s2, _) = await ApiCalls.GetAsync(_w.Client, $"/api/admin/employees/{_w.BEmployee}", _w.AToken);
        Assert.Equal(HttpStatusCode.NotFound, s2);
        var (s3, _) = await ApiCalls.GetAsync(_w.Client, $"/api/admin/employees/{_w.BEmployee}", _w.AToken, _w.BOrigin);
        Assert.Equal(HttpStatusCode.NotFound, s3);

        var (s4, bList) = await ApiCalls.GetAsync(_w.Client, "/api/admin/employees?pageSize=200", _w.BToken, _w.AOrigin);
        Assert.Equal(HttpStatusCode.OK, s4);
        // The list carries no e-mail column; the seed writes each person's e-mail as their name. Every row must be
        // one of B's own people, at B's branch — company A's admin must not appear.
        var bRows = Items(bList).ToList();
        Assert.NotEmpty(bRows);
        Assert.All(bRows, e =>
        {
            Assert.NotEqual("admin@a.test", e.GetProperty("fullName").GetString());
            Assert.Equal(_w.BLocation, e.GetProperty("locationId").GetGuid());
        });
    }

    [PostgresFact]
    public async Task Company_A_cannot_change_reset_or_delete_company_B_employees()
    {
        var update = new { fullName = "PWNED", email = (string?)null, locationId = _w.ALocation, role = "Employee", isActive = true };
        Assert.Equal(HttpStatusCode.NotFound,
            (await ApiCalls.SendAsync(_w.Client, HttpMethod.Put, $"/api/admin/employees/{_w.BEmployee}", _w.AToken, body: update)).Status);
        Assert.Equal(HttpStatusCode.NotFound,
            (await ApiCalls.SendAsync(_w.Client, HttpMethod.Post, $"/api/admin/employees/{_w.BEmployee}/reset-pin", _w.AToken, body: new { })).Status);
        Assert.Equal(HttpStatusCode.NotFound,
            (await ApiCalls.SendAsync(_w.Client, HttpMethod.Delete, $"/api/admin/employees/{_w.BEmployee}?force=true", _w.AToken)).Status);

        await using var db = Seed.Context(_w.Cs, _w.B);
        var still = await db.Employees.SingleAsync(e => e.Id == _w.BEmployee);
        Assert.Equal("worker@b.test", still.FullName);
        Assert.Equal(1, await db.AttendanceRecords.CountAsync(r => r.EmployeeId == _w.BEmployee));
    }

    [PostgresFact]
    public async Task Company_A_cannot_change_company_B_locations_or_revoke_their_posters()
    {
        int versionBefore;
        await using (var before = Seed.Context(_w.Cs, _w.B))
            versionBefore = (await before.Locations.SingleAsync(l => l.Id == _w.BLocation)).QrVersion;

        var body = new
        {
            name = "PWNED", latitude = 40.4, longitude = 49.8, radiusMeters = 100,
            shiftStart = "09:00", shiftEnd = "18:00", lateThresholdMinutes = 15, workDaysMask = 62,
        };
        Assert.Equal(HttpStatusCode.NotFound,
            (await ApiCalls.SendAsync(_w.Client, HttpMethod.Put, $"/api/admin/locations/{_w.BLocation}", _w.AToken, body: body)).Status);
        Assert.Equal(HttpStatusCode.NotFound,
            (await ApiCalls.SendAsync(_w.Client, HttpMethod.Post, $"/api/admin/locations/{_w.BLocation}/invalidate-qr", _w.AToken, body: new { })).Status);

        await using var db = Seed.Context(_w.Cs, _w.B);
        var loc = await db.Locations.SingleAsync(l => l.Id == _w.BLocation);
        Assert.Equal("B ofis", loc.Name);
        Assert.Equal(versionBefore, loc.QrVersion);
    }

    [PostgresFact]
    public async Task Company_A_cannot_create_a_person_inside_a_company_B_branch()
    {
        var invite = new
        {
            fullName = "Cross FK", email = (string?)null, locationId = _w.BLocation, role = "Employee",
            phoneNumber = "+994551230000", activateWithPin = true,
        };
        var (status, _) = await ApiCalls.SendAsync(_w.Client, HttpMethod.Post, "/api/admin/employees/invite", _w.AToken, body: invite);
        Assert.True(status is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.Forbidden, $"got {(int)status}");

        await using var db = Seed.Context(_w.Cs);
        Assert.False(await db.Employees.IgnoreQueryFilters().AnyAsync(e => e.FullName == "Cross FK"));
    }

    [PostgresFact]
    public async Task Company_A_reads_none_of_company_B_attendance_or_reports()
    {
        var (s1, att) = await ApiCalls.GetAsync(_w.Client, $"/api/attendance/employee/{_w.BEmployee}", _w.AToken);
        Assert.True(s1 is HttpStatusCode.NotFound or HttpStatusCode.Forbidden
                    || (s1 == HttpStatusCode.OK && att.ValueKind == JsonValueKind.Array && att.GetArrayLength() == 0), $"got {(int)s1} {att}");

        var from = DateTime.UtcNow.AddDays(-10).ToString("yyyy-MM-dd");
        var to = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var (s2, rep) = await ApiCalls.GetAsync(_w.Client, $"/api/reports/summary?from={from}&to={to}&locationId={_w.BLocation}", _w.AToken);
        Assert.True(s2 is HttpStatusCode.NotFound or HttpStatusCode.Forbidden
                    || (s2 == HttpStatusCode.OK && rep.GetProperty("rows").GetArrayLength() == 0), $"got {(int)s2} {rep}");
    }

    [PostgresFact]
    public async Task A_token_whose_company_claim_was_edited_or_unsigned_is_rejected()
    {
        var parts = _w.AToken.Split('.');
        var payload = JsonSerializer.Deserialize<Dictionary<string, object>>(B64UrlDecode(parts[1]))!;
        payload["tid"] = _w.B.ToString();
        var forged = parts[0] + "." + B64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload)) + "." + parts[2];
        Assert.Equal(HttpStatusCode.Unauthorized, (await ApiCalls.GetAsync(_w.Client, "/api/admin/employees", forged, _w.BOrigin)).Status);

        var none = B64UrlEncode(Encoding.UTF8.GetBytes("{\"alg\":\"none\",\"typ\":\"JWT\"}")) + "." + parts[1] + ".";
        Assert.Equal(HttpStatusCode.Unauthorized, (await ApiCalls.GetAsync(_w.Client, "/api/admin/employees", none, _w.AOrigin)).Status);
    }

    [PostgresFact]
    public async Task Development_endpoints_do_not_exist_in_production_and_anonymous_admin_calls_are_401()
    {
        Assert.Equal(HttpStatusCode.NotFound,
            (await ApiCalls.SendAsync(_w.Client, HttpMethod.Post, "/api/dev/seed", origin: _w.AOrigin, body: new { })).Status);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await ApiCalls.SendAsync(_w.Client, HttpMethod.Get, "/api/admin/employees", origin: _w.AOrigin)).Status);
    }

    private static IEnumerable<JsonElement> Items(JsonElement list)
        => (list.ValueKind == JsonValueKind.Object ? list.GetProperty("items") : list).EnumerateArray().ToList();

    private static string B64UrlEncode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] B64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
