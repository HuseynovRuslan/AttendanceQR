using System.Security.Claims;
using AttendanceQR.Api;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Application.Common;
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
/// Handing a company over without moving in: the operator creates it with the CUSTOMER's admin and sets
/// it up by impersonating that admin, leaving no row of their own inside the tenant.
///
/// Two things used to make that impossible, and both are pinned here:
///   • the console picked the OLDEST active admin and refused if that was an operator — which is
///     precisely the shape of every company an operator once set up by hand, so the door was shut on
///     the tenants that needed it most. It now skips past operators (and self) to the customer's admin.
///   • a freshly created admin is on a temporary PIN, and the PIN gate refused every request from that
///     account — impersonation included — so nothing could be configured on the day the company was
///     created. Impersonation is now exempt from the gate (Program.cs), which is only safe because the
///     session cannot touch the credential itself: set-initial-pin and change-password refuse it, so the
///     customer's own forced PIN change is still theirs to make, unconsumed.
///
/// If a test here fails, the handover is blocked again. What a borrowed session may NOT do — the
/// credential and identifier guards the exemption above rests on — is pinned in
/// ImpersonationCredentialGuardTests; this file does not cover them.
/// </summary>
public class ImpersonationHandoverTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000f7");

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public SuperAdminController Controller { get; }
        public Guid OperatorId { get; } = Guid.NewGuid();
        public Guid CustomerAdminId { get; } = Guid.NewGuid();
        public Guid SecondOperatorId { get; } = Guid.NewGuid();

        /// <param name="operatorIsOldest">The realistic shape: the operator's own row was created first,
        /// because they made the company by hand before the customer's admin existed.</param>
        /// <param name="withCustomerAdmin">When false the tenant has operators as admins and nobody else.</param>
        public Harness(bool operatorIsOldest = true, bool withCustomerAdmin = true)
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase($"imp-handover-{Guid.NewGuid()}").Options,
                tenant);

            Db.Tenants.Add(new Tenant
            {
                Id = TenantId, Name = "Musteri", Slug = "musteri", DisplayName = "Musteri", IsActive = true,
            });

            var baseTime = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);
            Db.Employees.Add(Admin(OperatorId, "Operator Ozu", baseTime));
            Db.Employees.Add(Admin(SecondOperatorId, "Ikinci Operator", baseTime.AddMinutes(5)));
            if (withCustomerAdmin)
            {
                Db.Employees.Add(Admin(
                    CustomerAdminId, "Musteri Admini",
                    operatorIsOldest ? baseTime.AddHours(1) : baseTime.AddHours(-1),
                    mustChangePin: true));
            }
            Db.SaveChanges();

            var options = new AppOptions
            {
                SuperAdminEmployeeIds = $"{OperatorId},{SecondOperatorId}",
            };

            Controller = new SuperAdminController(Db, tenant, new StubHasher(), new StubJwt(), options)
            {
                ControllerContext = new ControllerContext { HttpContext = OperatorContext(OperatorId) },
            };
        }

        private static Employee Admin(Guid id, string name, DateTime created, bool mustChangePin = false) => new()
        {
            Id = id,
            TenantId = TenantId,
            FullName = name,
            Role = EmployeeRole.Admin,
            IsActive = true,
            ActivatedAtUtc = created,
            CreatedAtUtc = created,
            PasswordHash = "h",
            MustChangePin = mustChangePin,
        };

        private static HttpContext OperatorContext(Guid operatorId) => new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("sub", operatorId.ToString()),
                new Claim("role", nameof(EmployeeRole.Admin)),
            }, "test")),
        };

        public void Dispose() => Db.Dispose();
    }

    private sealed class StubHasher : IPasswordHasher
    {
        public string Hash(string password) => "hashed:" + password;
        public bool Verify(string hash, string password) => hash == "hashed:" + password;
    }

    private sealed class StubJwt : IJwtService
    {
        public string GenerateToken(Employee employee) => "token";
        public string GenerateImpersonationToken(Employee employee, Guid impersonatedBy, int expiryMinutes)
            => $"imp-for:{employee.Id}";
    }

    private static string ValueOf(IActionResult result, string property)
    {
        var obj = Assert.IsAssignableFrom<ObjectResult>(result);
        return obj.Value?.GetType().GetProperty(property)?.GetValue(obj.Value)?.ToString() ?? "";
    }

    // --- who the console borrows --------------------------------------------------

    [Fact]
    public async Task It_skips_the_operators_own_row_and_borrows_the_customers_admin()
    {
        using var h = new Harness(operatorIsOldest: true);

        var result = await h.Controller.Impersonate(TenantId);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal($"imp-for:{h.CustomerAdminId}", ValueOf(result, "token"));
        Assert.Equal("Musteri Admini", ValueOf(result, "adminName"));
    }

    [Fact]
    public async Task It_still_borrows_the_customers_admin_when_that_row_is_the_older_one()
    {
        using var h = new Harness(operatorIsOldest: false);
        Assert.Equal($"imp-for:{h.CustomerAdminId}", ValueOf(await h.Controller.Impersonate(TenantId), "token"));
    }

    [Fact]
    public async Task A_company_whose_only_admins_are_operators_says_so_plainly()
    {
        // Not "NoAdmin" — there ARE admins. The operator needs to be told to create the customer's own,
        // not sent looking for an admin that is sitting right there.
        using var h = new Harness(withCustomerAdmin: false);

        var result = await h.Controller.Impersonate(TenantId);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<BadRequestObjectResult>(result).StatusCode);
        Assert.Equal("NoImpersonableAdmin", ValueOf(result, "error"));
    }

    [Fact]
    public async Task A_fellow_operator_is_never_borrowed()
    {
        using var h = new Harness(withCustomerAdmin: false);
        var result = await h.Controller.Impersonate(TenantId);

        // The second operator is an active admin of this tenant and is NOT the caller — the only thing
        // keeping it out of the token is the allowlist skip, which is the takeover fix.
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task An_empty_company_still_reports_NoAdmin()
    {
        using var h = new Harness(withCustomerAdmin: false);
        foreach (var e in h.Db.Employees.IgnoreQueryFilters().ToList())
            h.Db.Employees.Remove(e);
        h.Db.SaveChanges();

        Assert.Equal("NoAdmin", ValueOf(await h.Controller.Impersonate(TenantId), "error"));
    }

    // --- the company can see it happened -------------------------------------------

    [Fact]
    public async Task Borrowing_an_admin_writes_a_row_into_the_COMPANYS_own_audit()
    {
        // Everything the borrowed session then does is recorded under the admin's own id (AuditLog has
        // no impersonator field), and the operator console's log lives behind /api/super, which no
        // tenant may read. Without this row a company has no way of knowing the platform was ever
        // inside their account — which is exactly what enrolling an operator as a visible employee row
        // used to provide, and what this feature removes.
        using var h = new Harness();

        Assert.IsType<OkObjectResult>(await h.Controller.Impersonate(TenantId));

        var row = Assert.Single(h.Db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.EventType == AuditEventType.ImpersonationStarted).ToList());
        Assert.Equal(TenantId, row.TenantId);              // the TARGET company, not the operator's own
        Assert.Equal(h.CustomerAdminId, row.EmployeeId);
        Assert.Contains(h.OperatorId.ToString(), row.Reason);
    }

    // --- the borrowed account keeps its own credential -----------------------------

    [Fact]
    public void An_impersonation_token_is_recognisable_as_one()
    {
        // The whole exemption rests on this claim being present and readable, so pin it: the helper the
        // PIN gate and the credential endpoints both consult must see "imp".
        var impersonating = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("imp", Guid.NewGuid().ToString()),
        }, "test"));
        var ordinary = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("sub", Guid.NewGuid().ToString()),
        }, "test"));

        Assert.True(impersonating.IsImpersonating());
        Assert.False(ordinary.IsImpersonating());
    }
}
