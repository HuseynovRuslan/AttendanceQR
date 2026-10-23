using System.Security.Claims;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Application.Common;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The two account actions a manager may take company-wide since 2026-09-14 — a new temporary PIN and a
/// new login number — for any plain employee or fellow manager, and the lines that did NOT move.
///
/// The owner asked for it outright. What it must never turn back into is the 2026-08-08 takeover: an
/// ADMIN, a platform operator and the manager themself stay refused, and every use leaves an audit row
/// naming who did it. The full edit — branch, shift, switching someone off — keeps the branch rule.
/// </summary>
public class ManagerCredentialTests
{
    private static readonly Guid TenantA = Guid.Parse("00000000-0000-0000-0000-0000000000c1");
    private static readonly Guid TenantB = Guid.Parse("00000000-0000-0000-0000-0000000000c2");

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public ManagerController Controller { get; }
        public Guid BranchA { get; } = Guid.NewGuid();   // managed by the caller
        public Guid BranchB { get; } = Guid.NewGuid();   // same company, not managed
        public Guid ManagerId { get; } = Guid.NewGuid();
        public Guid SameBranchEmployeeId { get; } = Guid.NewGuid();
        public Guid OtherBranchEmployeeId { get; } = Guid.NewGuid();
        public Guid SameBranchManagerId { get; } = Guid.NewGuid();
        public Guid OtherBranchManagerId { get; } = Guid.NewGuid();
        public Guid SameBranchAdminId { get; } = Guid.NewGuid();
        public Guid OtherBranchAdminId { get; } = Guid.NewGuid();
        public Guid InactiveEmployeeId { get; } = Guid.NewGuid();
        public Guid OperatorId { get; } = Guid.NewGuid();
        public Guid OtherTenantEmployeeId { get; } = Guid.NewGuid();

        public Harness()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantA);
            Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"mgr-cred-{Guid.NewGuid()}").Options, tenant);

            Db.Tenants.Add(new Tenant { Id = TenantA, Name = "A", Slug = "a", DisplayName = "A", IsActive = true });
            Db.Tenants.Add(new Tenant { Id = TenantB, Name = "B", Slug = "b", DisplayName = "B", IsActive = true });
            Db.Locations.Add(Location(BranchA, "Filial A"));
            Db.Locations.Add(Location(BranchB, "Filial B"));
            Db.ManagedLocations.Add(new ManagedLocation { EmployeeId = ManagerId, LocationId = BranchA, TenantId = TenantA });

            Db.Employees.Add(Person(ManagerId, "Test Menecer Özü", EmployeeRole.Manager, BranchA, "501110000"));
            Db.Employees.Add(Person(SameBranchEmployeeId, "Test Əli", EmployeeRole.Employee, BranchA, "501110001"));
            Db.Employees.Add(Person(OtherBranchEmployeeId, "Test Vəli", EmployeeRole.Employee, BranchB, "501110002"));
            Db.Employees.Add(Person(SameBranchManagerId, "Test Həmkar Menecer", EmployeeRole.Manager, BranchA, "501110003"));
            Db.Employees.Add(Person(OtherBranchManagerId, "Test Uzaq Menecer", EmployeeRole.Manager, BranchB, "501110004"));
            Db.Employees.Add(Person(SameBranchAdminId, "Test Admin A", EmployeeRole.Admin, BranchA, "501110005"));
            Db.Employees.Add(Person(OtherBranchAdminId, "Test Admin B", EmployeeRole.Admin, BranchB, "501110006"));
            var inactive = Person(InactiveEmployeeId, "Test Deaktiv", EmployeeRole.Employee, BranchB, "501110007");
            inactive.IsActive = false;
            Db.Employees.Add(inactive);
            // An operator whose tenant row is a MANAGER — the shape the widened gate newly admits by role.
            Db.Employees.Add(Person(OperatorId, "Test Operator", EmployeeRole.Manager, BranchA, "501110008"));
            // Placed on BranchA's own id: only the tenant filter keeps this row out.
            Db.Employees.Add(Person(OtherTenantEmployeeId, "Test Başqa Şirkət", EmployeeRole.Employee, BranchA, "501110009", TenantB));
            Db.SaveChanges();

            var identity = new ClaimsIdentity(new[]
            {
                new Claim("sub", ManagerId.ToString()),
                new Claim("role", nameof(EmployeeRole.Manager)),
            }, "test");
            Controller = new ManagerController(Db, new StubHasher(), new StubSummary(),
                new AppOptions { SuperAdminEmployeeIds = OperatorId.ToString() })
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
                },
            };
        }

        private static Location Location(Guid id, string name) => new()
        {
            Id = id, TenantId = TenantA, Name = name,
            Latitude = 40.4093, Longitude = 49.8671, RadiusMeters = 150,
            ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
            LateThresholdMinutes = 15, QrVersion = 1, IsActive = true,
        };

        private static Employee Person(Guid id, string name, EmployeeRole role, Guid locationId, string phone, Guid? tenantId = null) => new()
        {
            Id = id, TenantId = tenantId ?? TenantA, FullName = name, Role = role, LocationId = locationId,
            PhoneNumber = phone, IsActive = true, ActivatedAtUtc = DateTime.UtcNow, PasswordHash = "original-hash",
        };

        public Employee Row(Guid id) => Db.Employees.IgnoreQueryFilters().AsNoTracking().Single(e => e.Id == id);

        public List<AuditLog> Audits(Guid id) => Db.AuditLogs.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.EmployeeId == id && a.EventType == AuditEventType.CredentialChangedByManager)
            .ToList();

        public void Dispose() => Db.Dispose();
    }

    private sealed class StubHasher : IPasswordHasher
    {
        public string Hash(string password) => "hashed:" + password;
        public bool Verify(string hash, string password) => hash == "hashed:" + password;
    }

    private sealed class StubSummary : IDailySummaryService
    {
        public Task<int> GenerateForDateAsync(DateOnly date, CancellationToken ct = default) => Task.FromResult(0);
    }

    private static void AssertStatus(IActionResult result, int status, string? error = null)
    {
        var obj = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(status, obj.StatusCode);
        if (error is not null)
            Assert.Contains(error, obj.Value!.ToString());
    }

    private static object? Prop(object o, string name) => o.GetType().GetProperty(name)?.GetValue(o);

    private static async Task<List<object>> Lookup(Harness h, string q)
    {
        var ok = Assert.IsType<OkObjectResult>(await h.Controller.CredentialTargets(q));
        return ((System.Collections.IEnumerable)ok.Value!).Cast<object>().ToList();
    }

    // --- PIN reset: who it now reaches -------------------------------------------

    [Fact]
    public async Task A_plain_employee_at_another_branch_can_have_their_PIN_reset()
    {
        using var h = new Harness();

        var ok = Assert.IsType<OkObjectResult>(await h.Controller.ResetPin(h.OtherBranchEmployeeId));

        var row = h.Row(h.OtherBranchEmployeeId);
        Assert.NotEqual("original-hash", row.PasswordHash);
        Assert.True(row.MustChangePin);
        Assert.Equal(1, row.TokenVersion);
        Assert.Contains("tempPin", ok.Value!.ToString());
    }

    [Fact]
    public async Task A_fellow_managers_PIN_is_reset_at_the_same_branch_and_not_another()
    {
        // The owner, 2026-09-18: a colleague is a manager at YOUR branch; another branch's managers are
        // not to be seen. Their answer is «not found», the same as somebody who does not exist.
        using var h = new Harness();

        Assert.IsType<OkObjectResult>(await h.Controller.ResetPin(h.SameBranchManagerId));
        AssertStatus(await h.Controller.ResetPin(h.OtherBranchManagerId), StatusCodes.Status404NotFound);

        Assert.NotEqual("original-hash", h.Row(h.SameBranchManagerId).PasswordHash);
        Assert.Equal("original-hash", h.Row(h.OtherBranchManagerId).PasswordHash);
    }

    [Fact]
    public async Task Every_reset_leaves_a_row_naming_the_manager_and_never_the_PIN()
    {
        using var h = new Harness();

        var ok = Assert.IsType<OkObjectResult>(await h.Controller.ResetPin(h.SameBranchManagerId));
        var tempPin = (string)Prop(ok.Value!, "tempPin")!;

        var audit = Assert.Single(h.Audits(h.SameBranchManagerId));
        Assert.Contains("PIN sıfırlandı", audit.Reason);
        Assert.Contains("Test Menecer Özü", audit.Reason);
        Assert.Contains(h.ManagerId.ToString(), audit.Reason);
        Assert.DoesNotContain(tempPin, audit.Reason);
    }

    // --- PIN reset: the lines that stay ------------------------------------------

    [Fact]
    public async Task An_admin_is_never_reachable_at_any_branch()
    {
        // THE line. A manager holding an admin's temporary PIN holds the company.
        using var h = new Harness();

        foreach (var admin in new[] { h.SameBranchAdminId, h.OtherBranchAdminId })
        {
            var result = await h.Controller.ResetPin(admin);
            AssertStatus(result, StatusCodes.Status403Forbidden, "ManagerCannotManageRole");
            Assert.DoesNotContain("tempPin", ((ObjectResult)result).Value!.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal("original-hash", h.Row(admin).PasswordHash);
            Assert.Equal(0, h.Row(admin).TokenVersion);
            Assert.Empty(h.Audits(admin));
        }
    }

    [Fact]
    public async Task A_manager_cannot_reset_their_own_PIN_this_way()
    {
        using var h = new Harness();

        AssertStatus(await h.Controller.ResetPin(h.ManagerId), StatusCodes.Status403Forbidden);
        Assert.Equal("original-hash", h.Row(h.ManagerId).PasswordHash);
    }

    [Fact]
    public async Task A_platform_operator_is_refused_even_when_their_row_is_a_manager()
    {
        // Admitting managers by role must not admit an operator who happens to sit in the tenant as one.
        using var h = new Harness();

        AssertStatus(await h.Controller.ResetPin(h.OperatorId), StatusCodes.Status403Forbidden, "CannotManageOperator");
        Assert.Equal("original-hash", h.Row(h.OperatorId).PasswordHash);
    }

    [Fact]
    public async Task Another_company_stays_invisible()
    {
        using var h = new Harness();

        AssertStatus(await h.Controller.ResetPin(h.OtherTenantEmployeeId), StatusCodes.Status404NotFound);
        Assert.Equal("original-hash", h.Row(h.OtherTenantEmployeeId).PasswordHash);
    }

    // --- the login number ----------------------------------------------------------

    [Fact]
    public async Task A_fellow_managers_number_is_changed_normalised_and_audited()
    {
        using var h = new Harness();

        var result = await h.Controller.ChangePhone(h.SameBranchManagerId, new ManagerPhoneChangeRequest("+994 50 777 66 55"));

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("507776655", h.Row(h.SameBranchManagerId).PhoneNumber);
        var audit = Assert.Single(h.Audits(h.SameBranchManagerId));
        Assert.Contains("0003", audit.Reason);          // the old number's tail
        Assert.Contains("6655", audit.Reason);          // the new one's
        Assert.Contains("Test Menecer Özü", audit.Reason);

        // Another branch's manager: untouched, and not even admitted to exist.
        AssertStatus(await h.Controller.ChangePhone(h.OtherBranchManagerId, new ManagerPhoneChangeRequest("0507776644")),
            StatusCodes.Status404NotFound);
        Assert.Equal("501110004", h.Row(h.OtherBranchManagerId).PhoneNumber);
    }

    [Fact]
    public async Task Changing_the_number_does_not_sign_anyone_out()
    {
        using var h = new Harness();

        await h.Controller.ChangePhone(h.OtherBranchEmployeeId, new ManagerPhoneChangeRequest("0507776655"));

        Assert.Equal(0, h.Row(h.OtherBranchEmployeeId).TokenVersion);
        Assert.Equal("original-hash", h.Row(h.OtherBranchEmployeeId).PasswordHash);
    }

    [Fact]
    public async Task A_number_somebody_else_already_signs_in_with_is_refused()
    {
        // No unique index on the column — a duplicate here silently hands one person's login to another.
        using var h = new Harness();

        var result = await h.Controller.ChangePhone(h.OtherBranchEmployeeId, new ManagerPhoneChangeRequest("050 111 00 01"));

        AssertStatus(result, StatusCodes.Status409Conflict, "PhoneAlreadyExists");
        Assert.Equal("501110002", h.Row(h.OtherBranchEmployeeId).PhoneNumber);
        Assert.Empty(h.Audits(h.OtherBranchEmployeeId));
    }

    [Fact]
    public async Task Too_few_digits_is_not_a_number()
    {
        using var h = new Harness();

        AssertStatus(await h.Controller.ChangePhone(h.OtherBranchEmployeeId, new ManagerPhoneChangeRequest("12-34")),
            StatusCodes.Status400BadRequest, "PhoneInvalid");
        Assert.Equal("501110002", h.Row(h.OtherBranchEmployeeId).PhoneNumber);
    }

    [Fact]
    public async Task Typing_the_same_number_differently_changes_nothing_and_logs_nothing()
    {
        using var h = new Harness();

        Assert.IsType<OkObjectResult>(await h.Controller.ChangePhone(h.OtherBranchEmployeeId, new ManagerPhoneChangeRequest("+994 50 111 00 02")));

        Assert.Empty(h.Audits(h.OtherBranchEmployeeId));
    }

    [Fact]
    public async Task An_admins_number_is_not_a_managers_to_change()
    {
        using var h = new Harness();

        AssertStatus(await h.Controller.ChangePhone(h.OtherBranchAdminId, new ManagerPhoneChangeRequest("0507776655")),
            StatusCodes.Status403Forbidden);
        Assert.Equal("501110006", h.Row(h.OtherBranchAdminId).PhoneNumber);
    }

    [Fact]
    public async Task Another_companys_number_cannot_be_reached()
    {
        using var h = new Harness();

        AssertStatus(await h.Controller.ChangePhone(h.OtherTenantEmployeeId, new ManagerPhoneChangeRequest("0507776655")),
            StatusCodes.Status404NotFound);
        Assert.Equal("501110009", h.Row(h.OtherTenantEmployeeId).PhoneNumber);
    }

    // --- only the credentials widened ----------------------------------------------

    [Fact]
    public async Task The_full_edit_still_keeps_the_branch_rule()
    {
        // The seam this whole change rests on: the same two people whose PIN a manager may now reset stay
        // out of reach for moving, re-shifting or switching off.
        using var h = new Harness();

        AssertStatus(await h.Controller.UpdateEmployee(h.OtherBranchEmployeeId,
            new ManagerEmployeeRequest(FullName: "Köçürülmüş", Email: null, PhoneNumber: null, FatherName: null, Position: null, LocationId: h.BranchA)),
            StatusCodes.Status404NotFound);
        // (A fellow manager IS fully editable since 2026-09-18 — ManagerAccountScopeTests pins that.)

        Assert.Equal("Test Vəli", h.Row(h.OtherBranchEmployeeId).FullName);
    }

    // --- finding the person --------------------------------------------------------

    [Fact]
    public async Task One_letter_finds_nobody()
    {
        // Not a search — a directory dump.
        using var h = new Harness();

        Assert.Empty(await Lookup(h, "T"));
        Assert.Empty(await Lookup(h, "  "));
    }

    [Fact]
    public async Task The_search_reaches_staff_everywhere_managers_at_home_and_nobody_it_must_not()
    {
        // Plain staff company-wide (2026-09-14); a fellow manager only at the caller's own branches
        // (2026-09-18 — «qıraq filialların menecerlərini görməməlidir»).
        using var h = new Harness();

        var ids = (await Lookup(h, "test")).Select(r => (Guid)Prop(r, "id")!).ToHashSet();

        Assert.Equal(
            new HashSet<Guid> { h.SameBranchEmployeeId, h.OtherBranchEmployeeId, h.SameBranchManagerId },
            ids);
    }

    [Fact]
    public async Task A_result_carries_only_the_last_four_digits()
    {
        using var h = new Harness();

        var row = Assert.Single(await Lookup(h, "vəli"));

        Assert.Equal("0002", Prop(row, "phoneTail"));
        Assert.DoesNotContain("501110002", row.ToString());
        Assert.Equal("Filial B", Prop(row, "locationName"));
    }

    [Fact]
    public async Task A_fellow_manager_is_marked_as_one()
    {
        using var h = new Harness();

        var row = Assert.Single(await Lookup(h, "həmkar"));

        Assert.Equal(true, Prop(row, "isManager"));
        // And the one at another branch is not there to be marked.
        Assert.Empty(await Lookup(h, "uzaq"));
    }

    [Fact]
    public async Task The_number_itself_finds_the_person()
    {
        using var h = new Harness();

        var row = Assert.Single(await Lookup(h, "110003"));

        Assert.Equal(h.SameBranchManagerId, (Guid)Prop(row, "id")!);
        // Another branch's manager cannot be found by their number either.
        Assert.Empty(await Lookup(h, "110004"));
    }
}
