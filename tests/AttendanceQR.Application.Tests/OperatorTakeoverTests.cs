using System.Security.Claims;
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
/// Nobody may reset a PLATFORM OPERATOR's PIN — because reset-pin hands the plaintext straight back in
/// the response, so whoever can call it on an operator can sign in as that operator.
///
/// There were three doors into that, and an operator is reachable through all of them: they are
/// allowlisted by employee id and normally sit inside some tenant as an ordinary-looking row.
///   • the operator console  — a Support-scoped operator resetting a FULL operator (audit item 3)
///   • the tenant admin      — or a support session impersonating one (audit item 3, tenant side)
///   • the branch manager    — if the operator's row happens to be Role==Employee in their branch
/// All three now refuse with CannotManageOperator. If any of these tests fails, that ladder is back.
/// </summary>
public class OperatorTakeoverTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public AppOptions Options { get; }
        public Guid Branch { get; } = Guid.NewGuid();
        /// <summary>A platform operator who also lives in this tenant — the realistic shape.</summary>
        public Guid OperatorId { get; } = Guid.NewGuid();
        public Guid TenantAdminId { get; } = Guid.NewGuid();
        public Guid ManagerId { get; } = Guid.NewGuid();
        public Guid PlainEmployeeId { get; } = Guid.NewGuid();

        public Harness(EmployeeRole operatorRole = EmployeeRole.Admin)
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"op-takeover-{Guid.NewGuid()}").Options, tenant);

            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "A", Slug = "a", DisplayName = "A", IsActive = true });
            Db.Locations.Add(new Location
            {
                Id = Branch, TenantId = TenantId, Name = "Filial", Latitude = 40.4, Longitude = 49.8,
                RadiusMeters = 150, ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
                LateThresholdMinutes = 15, QrVersion = 1, IsActive = true,
            });
            Db.ManagedLocations.Add(new ManagedLocation { EmployeeId = ManagerId, LocationId = Branch, TenantId = TenantId });
            Db.Employees.Add(Person(OperatorId, "Operator", operatorRole));
            Db.Employees.Add(Person(TenantAdminId, "Tenant Admin", EmployeeRole.Admin));
            Db.Employees.Add(Person(ManagerId, "Menecer", EmployeeRole.Manager));
            Db.Employees.Add(Person(PlainEmployeeId, "İşçi", EmployeeRole.Employee));
            Db.SaveChanges();

            Options = new AppOptions { SuperAdminEmployeeIds = OperatorId.ToString() };
        }

        private Employee Person(Guid id, string name, EmployeeRole role) => new()
        {
            Id = id, TenantId = TenantId, FullName = name, Role = role, LocationId = Branch,
            IsActive = true, ActivatedAtUtc = DateTime.UtcNow, PasswordHash = "original-hash",
        };

        private static ClaimsPrincipal Principal(Guid id, EmployeeRole role) =>
            new(new ClaimsIdentity(new[]
            {
                new Claim("sub", id.ToString()),
                new Claim("role", role.ToString()),
            }, "test"));

        public AdminController Admin(Guid callerId) =>
            new(Db, Microsoft.Extensions.Options.Options.Create(new InvitationOptions()),
                new StubHasher(), new StubLockout(), Options)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = Principal(callerId, EmployeeRole.Admin) },
                },
            };

        public ManagerController Manager() =>
            new(Db, new StubHasher(), new StubSummary(), Options)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = Principal(ManagerId, EmployeeRole.Manager) },
                },
            };

        public Employee Row(Guid id) => Db.Employees.IgnoreQueryFilters().AsNoTracking().Single(e => e.Id == id);

        public void Dispose() => Db.Dispose();
    }

    private sealed class StubHasher : IPasswordHasher
    {
        public string Hash(string password) => "hashed:" + password;
        public bool Verify(string hash, string password) => hash == "hashed:" + password;
    }

    private sealed class StubLockout : ILoginLockoutStore
    {
        public int LockoutMinutes => 15;
        public bool IsLockedOut(string key) => false;
        public int RecordFailure(string key) => 5;
        public void RecordSuccess(string key) { }
    }

    private sealed class StubSummary : IDailySummaryService
    {
        public Task<int> GenerateForDateAsync(DateOnly date, CancellationToken ct = default) => Task.FromResult(0);
    }

    private static void AssertForbidden(IActionResult result)
    {
        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, obj.StatusCode);
        Assert.Contains("CannotManageOperator", obj.Value!.ToString());
    }

    // --- the tenant admin door (and, through it, an impersonating support session) ---------------

    [Fact]
    public async Task A_tenant_admin_cannot_reset_a_platform_operators_pin()
    {
        using var h = new Harness();

        AssertForbidden(await h.Admin(h.TenantAdminId).ResetPin(h.OperatorId));

        // No temp PIN was minted and the credential never moved — the response cannot be mined either.
        Assert.Equal("original-hash", h.Row(h.OperatorId).PasswordHash);
        Assert.Equal(0, h.Row(h.OperatorId).TokenVersion);
        Assert.False(h.Row(h.OperatorId).MustChangePin);
    }

    [Fact]
    public async Task A_tenant_admin_can_still_reset_an_ordinary_employees_pin()
    {
        // The guard must not cost the tenant its own support workflow.
        using var h = new Harness();

        var ok = Assert.IsType<OkObjectResult>(await h.Admin(h.TenantAdminId).ResetPin(h.PlainEmployeeId));
        Assert.Contains("tempPin", ok.Value!.ToString());
        Assert.NotEqual("original-hash", h.Row(h.PlainEmployeeId).PasswordHash);
    }

    [Fact]
    public async Task The_refusal_comes_before_the_activation_check_so_it_cannot_be_used_to_probe()
    {
        // A never-activated operator must answer the same 403, not Conflict(NotActivated) — otherwise
        // the difference between the two responses tells an admin which employees are operators.
        using var h = new Harness();
        var op = h.Db.Employees.Single(e => e.Id == h.OperatorId);
        op.ActivatedAtUtc = null;
        h.Db.SaveChanges();

        AssertForbidden(await h.Admin(h.TenantAdminId).ResetPin(h.OperatorId));
    }

    // --- the branch manager door ------------------------------------------------------------------

    [Fact]
    public async Task A_manager_cannot_reset_a_platform_operator_sitting_in_their_branch()
    {
        // Role==Employee and the right branch both pass the manager's own scope rule — this is the
        // one shape where the operator slips through it.
        using var h = new Harness(operatorRole: EmployeeRole.Employee);

        AssertForbidden(await h.Manager().ResetPin(h.OperatorId));
        Assert.Equal("original-hash", h.Row(h.OperatorId).PasswordHash);
    }

    [Fact]
    public async Task A_manager_can_still_reset_their_own_branch_employees_pin()
    {
        using var h = new Harness(operatorRole: EmployeeRole.Employee);

        Assert.IsType<OkObjectResult>(await h.Manager().ResetPin(h.PlainEmployeeId));
        Assert.NotEqual("original-hash", h.Row(h.PlainEmployeeId).PasswordHash);
    }
}
