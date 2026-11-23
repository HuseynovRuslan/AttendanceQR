using System.Net;
using System.Text.Json;
using AttendanceQR.Domain.Enums;
using AttendanceQR.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.IntegrationTests.Baseline;

/// <summary>
/// Inside one company: a manager who manages branch L1 only, an employee at L1 and one at L2. Pins the branch
/// scope as it works today (verified by hand in the audit), over HTTP, so later packages cannot widen it by
/// accident. The one deliberately company-wide manager action (PIN reset, owner's rule of 2026-09-14) is pinned
/// as CURRENT behaviour — changing it is business decision D3, not a bug fix.
/// </summary>
public class ScopeBaselineTests : IClassFixture<ScopeBaselineTests.World>
{
    public sealed class World : IAsyncLifetime
    {
        public const string Pin = "4821";
        public string Cs = "";
        public QrLogApiFactory Factory = null!;
        public HttpClient Client = null!;
        public Guid T = Seed.OriginalTenantId, L1, L2, Manager, E1, E2;
        public string AdminToken = "", ManagerToken = "", EmployeeToken = "";

        public async Task InitializeAsync()
        {
            if (!PostgresAvailability.CanRun(out _)) return;
            Cs = await PostgresServer.CreateDatabaseAsync();
            L1 = await Seed.LocationAsync(Cs, T, "Filial 1");
            L2 = await Seed.LocationAsync(Cs, T, "Filial 2");
            await Seed.EmployeeAsync(Cs, T, L1, "admin@t.test", Pin, EmployeeRole.Admin);
            Manager = await Seed.EmployeeAsync(Cs, T, L1, "manager@t.test", Pin, EmployeeRole.Manager);
            await Seed.ManagesAsync(Cs, T, Manager, L1);
            E1 = await Seed.EmployeeAsync(Cs, T, L1, "e1@t.test", Pin);
            E2 = await Seed.EmployeeAsync(Cs, T, L2, "e2@t.test", Pin);

            Factory = new QrLogApiFactory(Cs);
            Client = Factory.CreateClient();
            AdminToken = await ApiCalls.LoginAsync(Client, Seed.OriginalTenantSlug, "admin@t.test", Pin);
            ManagerToken = await ApiCalls.LoginAsync(Client, Seed.OriginalTenantSlug, "manager@t.test", Pin);
            EmployeeToken = await ApiCalls.LoginAsync(Client, Seed.OriginalTenantSlug, "e1@t.test", Pin);
        }

        public async Task DisposeAsync()
        {
            if (Factory is not null) await Factory.DisposeAsync();
        }
    }

    private readonly World _w;
    public ScopeBaselineTests(World world) => _w = world;

    [PostgresFact]
    public async Task A_manager_reaches_their_own_branch()
    {
        Assert.Equal(HttpStatusCode.OK, (await ApiCalls.GetAsync(_w.Client, $"/api/manager/employees/{_w.E1}", _w.ManagerToken)).Status);
    }

    [PostgresFact]
    public async Task A_manager_cannot_open_change_or_report_on_another_branch()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await ApiCalls.GetAsync(_w.Client, $"/api/manager/employees/{_w.E2}", _w.ManagerToken)).Status);

        var put = await ApiCalls.SendAsync(_w.Client, HttpMethod.Put, $"/api/manager/employees/{_w.E2}", _w.ManagerToken,
            body: new { fullName = "PWNED", locationId = _w.L2 });
        Assert.True(put.Status is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest, $"got {(int)put.Status}");

        Assert.Equal(HttpStatusCode.Forbidden, (await ApiCalls.GetAsync(_w.Client, $"/api/attendance/employee/{_w.E2}", _w.ManagerToken)).Status);

        var from = DateTime.UtcNow.AddDays(-10).ToString("yyyy-MM-dd");
        var to = DateTime.UtcNow.ToString("yyyy-MM-dd");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await ApiCalls.GetAsync(_w.Client, $"/api/reports/summary?from={from}&to={to}&locationId={_w.L2}", _w.ManagerToken)).Status);

        var create = await ApiCalls.SendAsync(_w.Client, HttpMethod.Post, "/api/manager/employees", _w.ManagerToken,
            body: new { fullName = "Yeni", locationId = _w.L2, phoneNumber = "+994551239999" });
        Assert.Equal(HttpStatusCode.Forbidden, create.Status);

        await using var db = Seed.Context(_w.Cs, _w.T);
        Assert.Equal("e2@t.test", (await db.Employees.SingleAsync(e => e.Id == _w.E2)).FullName);
        Assert.False(await db.Employees.AnyAsync(e => e.FullName == "Yeni"));
    }

    [PostgresFact]
    public async Task A_manager_cannot_use_admin_endpoints_or_promote_anyone()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await ApiCalls.GetAsync(_w.Client, "/api/admin/employees", _w.ManagerToken)).Status);

        var promote = await ApiCalls.SendAsync(_w.Client, HttpMethod.Put, $"/api/admin/employees/{_w.Manager}", _w.ManagerToken,
            body: new { fullName = "manager@t.test", email = "manager@t.test", locationId = _w.L1, role = "Admin", isActive = true });
        Assert.Equal(HttpStatusCode.Forbidden, promote.Status);

        await ApiCalls.SendAsync(_w.Client, HttpMethod.Put, $"/api/manager/employees/{_w.E1}", _w.ManagerToken,
            body: new { fullName = "e1@t.test", locationId = _w.L1, role = "Admin" });

        await using var db = Seed.Context(_w.Cs, _w.T);
        Assert.Equal(EmployeeRole.Manager, (await db.Employees.SingleAsync(e => e.Id == _w.Manager)).Role);
        Assert.Equal(EmployeeRole.Employee, (await db.Employees.SingleAsync(e => e.Id == _w.E1)).Role);
    }

    [PostgresFact]
    public async Task An_employee_reaches_no_admin_or_manager_surface_and_sees_only_themselves_on_the_board()
    {
        foreach (var path in new[] { "/api/admin/employees", "/api/manager/employees", $"/api/attendance/employee/{_w.E2}",
                     "/api/admin/attendance/open" })
        {
            var (status, _) = await ApiCalls.GetAsync(_w.Client, path, _w.EmployeeToken);
            Assert.True(status is HttpStatusCode.Forbidden or HttpStatusCode.NotFound, $"{path} → {(int)status}");
        }

        var (s, board) = await ApiCalls.GetAsync(_w.Client, "/api/reports/today", _w.EmployeeToken);
        Assert.Equal(HttpStatusCode.OK, s);
        var ids = board.EnumerateArray().Select(r => r.GetProperty("employeeId").GetGuid()).ToList();
        Assert.All(ids, id => Assert.Equal(_w.E1, id));
    }

    [PostgresFact]
    public async Task Current_rule_SEC06_a_manager_can_reset_the_PIN_of_a_plain_employee_in_another_branch()
    {
        // CURRENT behaviour, pinned so that a change to it is deliberate (business decision D3), not accidental.
        // The audit flags it (SEC-06): the manager is handed the plaintext temporary PIN.
        var (status, body) = await ApiCalls.SendAsync(_w.Client, HttpMethod.Post, $"/api/manager/employees/{_w.E2}/reset-pin",
            _w.ManagerToken, body: new { });
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(JsonValueKind.String, body.GetProperty("tempPin").ValueKind);

        await using var db = Seed.Context(_w.Cs, _w.T);
        Assert.True(await db.AuditLogs.AnyAsync(a => a.EmployeeId == _w.E2 && a.EventType == AuditEventType.CredentialChangedByManager),
            "the reset must be written to the audit log against the account it changed");
    }
}
