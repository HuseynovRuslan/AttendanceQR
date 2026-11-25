using System.Security.Claims;
using System.Text.Json;
using AttendanceQR.Api;
using AttendanceQR.Api.Contracts;
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
/// The operator console gives roles too: «Admin təyin et» either creates a company admin or hands an unclaimed
/// admin account (no phone, no e-mail — nobody can sign in as it) to a person. Both are recorded in the
/// CUSTOMER's own log, naming the operator, in the same save as the account. RoleChangeAuditTests covers the
/// company's own admin panel.
/// </summary>
public class RoleChangeAuditOperatorTests
{
    private static readonly Guid OperatorTenant = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    private static readonly Guid CustomerTenant = Guid.Parse("00000000-0000-0000-0000-0000000000b2");

    private sealed class World : IDisposable
    {
        public AppDbContext Db { get; }
        public SuperAdminController Console { get; }
        public Guid OperatorId { get; } = Guid.NewGuid();
        public Guid? UnclaimedId { get; }

        public World(bool withUnclaimedAdmin)
        {
            var tenant = new TenantContext();
            tenant.Resolve(OperatorTenant);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"role-audit-op-{Guid.NewGuid()}").Options,
                tenant);

            Db.Tenants.Add(new Tenant { Id = OperatorTenant, Name = "Op", Slug = "op", DisplayName = "Op", IsActive = true });
            Db.Tenants.Add(new Tenant { Id = CustomerTenant, Name = "Musteri", Slug = "musteri", DisplayName = "Musteri", IsActive = true });
            var opBranch = new Location { TenantId = OperatorTenant, Name = "Ofis", IsActive = true, RadiusMeters = 150 };
            var customerBranch = new Location { TenantId = CustomerTenant, Name = "Baş ofis", IsActive = true, RadiusMeters = 150 };
            Db.Locations.AddRange(opBranch, customerBranch);
            Db.Employees.Add(new Employee
            {
                Id = OperatorId, TenantId = OperatorTenant, FullName = "Operator", Role = EmployeeRole.Admin,
                IsActive = true, PasswordHash = "h", LocationId = opBranch.Id, ActivatedAtUtc = DateTime.UtcNow,
            });
            if (withUnclaimedAdmin)
            {
                var unclaimed = new Employee
                {
                    TenantId = CustomerTenant, FullName = "Admin", Role = EmployeeRole.Admin, IsActive = true,
                    PasswordHash = "h", LocationId = customerBranch.Id, ActivatedAtUtc = DateTime.UtcNow,
                };
                Db.Employees.Add(unclaimed);
                UnclaimedId = unclaimed.Id;
            }
            Db.SaveChanges();

            Console = new SuperAdminController(Db, tenant, new StubHasher(), new StubJwt(),
                new AppOptions { SuperAdminEmployeeIds = OperatorId.ToString() })
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                        {
                            new Claim("sub", OperatorId.ToString()),
                            new Claim("role", nameof(EmployeeRole.Admin)),
                        }, "test")),
                    },
                },
            };
        }

        public List<AuditLog> Rows() => Db.AuditLogs.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.EventType == AuditEventType.EmployeeRoleChanged)
            .ToList();

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
        public string GenerateImpersonationToken(Employee employee, Guid impersonatedBy, bool readOnly = false) => "imp";
    }

    [Fact]
    public async Task A_new_company_admin_is_logged_in_the_customers_company_naming_the_operator()
    {
        using var w = new World(withUnclaimedAdmin: false);

        var result = await w.Console.SetTenantAdmin(CustomerTenant,
            new SetTenantAdminRequest("+994553334455", "Müştəri Admini", "4826"));

        Assert.IsType<OkObjectResult>(result);
        var row = Assert.Single(w.Rows());
        Assert.Equal(CustomerTenant, row.TenantId);
        var created = w.Db.Employees.IgnoreQueryFilters().AsNoTracking()
            .Single(e => e.TenantId == CustomerTenant && e.FullName == "Müştəri Admini");
        Assert.Equal(created.Id, row.EmployeeId);

        var body = JsonDocument.Parse(row.Reason!).RootElement;
        Assert.Equal("created", body.GetProperty("action").GetString());
        Assert.Equal(w.OperatorId, body.GetProperty("by").GetGuid());
        Assert.Equal("Admin", body.GetProperty("to").GetProperty("role").GetString());
        Assert.Equal(RoleChangeAudit.Via.OperatorTenantAdmin, body.GetProperty("via").GetString());
        Assert.DoesNotContain("4826", row.Reason);
        Assert.DoesNotContain("553334455", row.Reason);
        Assert.DoesNotContain("hashed:", row.Reason);
    }

    [Fact]
    public async Task Handing_over_an_unclaimed_admin_account_is_logged_as_claimed()
    {
        using var w = new World(withUnclaimedAdmin: true);

        var result = await w.Console.SetTenantAdmin(CustomerTenant,
            new SetTenantAdminRequest("+994553334466", "Sahib", "5937"));

        Assert.IsType<OkObjectResult>(result);
        var row = Assert.Single(w.Rows());
        Assert.Equal(w.UnclaimedId, row.EmployeeId);
        var body = JsonDocument.Parse(row.Reason!).RootElement;
        Assert.Equal("claimed", body.GetProperty("action").GetString());
        Assert.Equal("Admin", body.GetProperty("from").GetProperty("role").GetString());
        Assert.Equal("Admin", body.GetProperty("to").GetProperty("role").GetString());
        Assert.DoesNotContain("5937", row.Reason);
    }
}
