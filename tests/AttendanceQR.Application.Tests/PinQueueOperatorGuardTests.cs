using System.Security.Claims;
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
/// SEC-02 (2026-10-02 audit, fixed 2026-10-15). A platform operator's employee row sits inside a company
/// like anybody's — today in bax, beside six admins — and «PIN-i unutdum» can be filed for any number,
/// anonymously. Resolving that request from the company's queue handed back the operator's temporary PIN,
/// and with it the console over every company. Every other way of minting a PIN refused an operator; the
/// queue did not.
/// </summary>
public class PinQueueOperatorGuardTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000005e2");

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public Guid Branch { get; } = Guid.NewGuid();
        public Guid AdminId { get; } = Guid.NewGuid();
        public Guid ManagerId { get; } = Guid.NewGuid();
        public Guid OperatorId { get; } = Guid.NewGuid();
        public Guid StaffId { get; } = Guid.NewGuid();
        private readonly AppOptions _options;

        public Harness(EmployeeRole operatorRowRole)
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"pin-queue-operator-{Guid.NewGuid()}").Options, tenant);
            _options = new AppOptions { SuperAdminEmployeeIds = OperatorId.ToString() };

            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "Bax", Slug = "bax", DisplayName = "Bax", IsActive = true });
            Db.Locations.Add(new Location { Id = Branch, TenantId = TenantId, Name = "Filial", IsActive = true, WorkDaysMask = 127, QrVersion = 1 });
            Person(AdminId, "Şirkət Admini", EmployeeRole.Admin);
            Person(ManagerId, "Filial Meneceri", EmployeeRole.Manager);
            Person(OperatorId, "Platforma Operatoru", operatorRowRole);
            Person(StaffId, "Adi İşçi", EmployeeRole.Employee);
            Db.ManagedLocations.Add(new ManagedLocation { TenantId = TenantId, EmployeeId = ManagerId, LocationId = Branch });
            Db.SaveChanges();
        }

        private void Person(Guid id, string name, EmployeeRole role) => Db.Employees.Add(new Employee
        {
            Id = id, TenantId = TenantId, FullName = name, Role = role, LocationId = Branch, IsActive = true,
            ActivatedAtUtc = DateTime.UtcNow.AddDays(-30), PasswordHash = "hashed:1234", Email = $"{id:N}@t.local",
        });

        public Guid File(Guid employeeId)
        {
            var request = new PinResetRequest { TenantId = TenantId, EmployeeId = employeeId };
            Db.PinResetRequests.Add(request);
            Db.SaveChanges();
            return request.Id;
        }

        public AdminPinResetController As(Guid who, EmployeeRole role) =>
            new(Db, new Hasher(), new Lockout(), _options)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(
                            [new Claim("sub", who.ToString()), new Claim("role", role.ToString())], "test")),
                    },
                },
            };

        public Employee Row(Guid id) => Db.Employees.AsNoTracking().Single(e => e.Id == id);
        public PinResetRequest Request(Guid id) => Db.PinResetRequests.AsNoTracking().Single(r => r.Id == id);
        public void Dispose() => Db.Dispose();
    }

    private sealed class Hasher : IPasswordHasher
    {
        public string Hash(string password) => "hashed:" + password;
        public bool Verify(string hash, string password) => hash == "hashed:" + password;
    }

    private sealed class Lockout : ILoginLockoutStore
    {
        public int LockoutMinutes => 15;
        public bool IsLockedOut(string key) => false;
        public int RecordFailure(string key) => 4;
        public void RecordSuccess(string key) { }
    }

    private static void AssertCannotManageOperator(IActionResult result)
    {
        var refused = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);
        Assert.Contains("CannotManageOperator", refused.Value!.ToString());
    }

    [Theory]
    [InlineData(EmployeeRole.Admin)]     // how operator rows look in bax today
    [InlineData(EmployeeRole.Employee)]
    public async Task A_company_admin_cannot_resolve_an_operators_request(EmployeeRole operatorRow)
    {
        using var h = new Harness(operatorRow);
        var request = h.File(h.OperatorId);

        AssertCannotManageOperator(await h.As(h.AdminId, EmployeeRole.Admin).Resolve(request));

        // Nothing consumed, nothing minted.
        Assert.Equal(PinResetStatus.Pending, h.Request(request).Status);
        Assert.Equal("hashed:1234", h.Row(h.OperatorId).PasswordHash);
    }

    [Fact]
    public async Task Nor_can_the_manager_whose_branch_holds_the_operators_row()
    {
        using var h = new Harness(EmployeeRole.Employee);
        var request = h.File(h.OperatorId);

        AssertCannotManageOperator(await h.As(h.ManagerId, EmployeeRole.Manager).Resolve(request));
        Assert.Equal("hashed:1234", h.Row(h.OperatorId).PasswordHash);
    }

    [Fact]
    public async Task The_queue_does_not_offer_an_operators_request()
    {
        using var h = new Harness(EmployeeRole.Employee);
        h.File(h.OperatorId);
        h.File(h.StaffId);

        var ok = Assert.IsType<OkObjectResult>(await h.As(h.AdminId, EmployeeRole.Admin).Pending());
        var names = ((System.Collections.IEnumerable)ok.Value!).Cast<object>()
            .Select(r => (string)r.GetType().GetProperty("employeeName")!.GetValue(r)!).ToList();

        Assert.Equal(["Adi İşçi"], names);
    }
}
