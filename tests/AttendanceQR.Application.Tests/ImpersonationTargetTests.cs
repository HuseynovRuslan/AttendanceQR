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
/// WHOSE session the operator console may borrow, now that it can be chosen.
///
/// The button used to take the founding admin and nothing else, which answers an admin's question and
/// none of a manager's — and half the support calls are about a manager's screen: a branch gate, an
/// empty employee list, a leave they cannot file. Borrowing the admin to reproduce those shows the one
/// view guaranteed not to have the problem.
///
/// The widening is downward, and the ceiling is the part to defend. A manager is a SMALLER session
/// than an admin, so offering them takes nothing away. A plain employee would not be: that session can
/// create ATTENDANCE, and a support login must never be able to put a day's work on somebody's record.
/// The two older refusals — an allowlisted operator (the takeover fix) and self — still hold on the
/// chosen path exactly as they do on the automatic one.
/// </summary>
public class ImpersonationTargetTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000f9");
    private static readonly Guid OtherTenantId = Guid.Parse("00000000-0000-0000-0000-0000000000fa");

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public SuperAdminController Controller { get; }
        public Guid OperatorId { get; } = Guid.NewGuid();
        public Guid CustomerAdminId { get; } = Guid.NewGuid();
        public Guid ManagerId { get; } = Guid.NewGuid();
        public Guid WorkerId { get; } = Guid.NewGuid();
        public Guid OtherTenantAdminId { get; } = Guid.NewGuid();
        public Guid BranchId { get; } = Guid.NewGuid();

        public Harness()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase($"imp-target-{Guid.NewGuid()}").Options,
                tenant);

            Db.Tenants.Add(new Tenant
            {
                Id = TenantId, Name = "Musteri", Slug = "musteri", DisplayName = "Musteri", IsActive = true,
            });
            Db.Tenants.Add(new Tenant
            {
                Id = OtherTenantId, Name = "Basqa", Slug = "basqa", DisplayName = "Basqa", IsActive = true,
            });

            Db.Locations.Add(new Location
            {
                Id = BranchId, TenantId = TenantId, Name = "Camasirxana",
                Latitude = 40.4, Longitude = 49.8, RadiusMeters = 150,
                ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
                LateThresholdMinutes = 15, QrVersion = 1, IsActive = true,
            });

            var t0 = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);
            Db.Employees.Add(Person(OperatorId, "Operator Ozu", EmployeeRole.Admin, t0));
            Db.Employees.Add(Person(CustomerAdminId, "Musteri Admini", EmployeeRole.Admin, t0.AddHours(1)));
            Db.Employees.Add(Person(ManagerId, "Filial Meneceri", EmployeeRole.Manager, t0.AddHours(2)));
            Db.Employees.Add(Person(WorkerId, "Adi Isci", EmployeeRole.Employee, t0.AddHours(3)));
            Db.Employees.Add(new Employee
            {
                Id = OtherTenantAdminId, TenantId = OtherTenantId, FullName = "Basqa Sirket Admini",
                Role = EmployeeRole.Admin, IsActive = true, PasswordHash = "h",
                LocationId = Guid.NewGuid(), CreatedAtUtc = t0, ActivatedAtUtc = t0,
            });

            Db.ManagedLocations.Add(new ManagedLocation
            {
                EmployeeId = ManagerId, LocationId = BranchId, TenantId = TenantId,
            });
            Db.SaveChanges();

            var options = new AppOptions { SuperAdminEmployeeIds = OperatorId.ToString() };
            Controller = new SuperAdminController(Db, tenant, new StubHasher(), new StubJwt(), options)
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

        private Employee Person(Guid id, string name, EmployeeRole role, DateTime created) => new()
        {
            Id = id, TenantId = TenantId, FullName = name, Role = role, IsActive = true,
            PasswordHash = "h", LocationId = BranchId, CreatedAtUtc = created, ActivatedAtUtc = created,
        };

        public async Task<List<object>> TargetsAsync()
        {
            var ok = Assert.IsType<OkObjectResult>(await Controller.ImpersonationTargets(TenantId));
            return Assert.IsAssignableFrom<IEnumerable<object>>(ok.Value).ToList();
        }

        public static T Read<T>(object row, string name) => (T)row.GetType().GetProperty(name)!.GetValue(row)!;

        public static string ErrorOf(IActionResult result)
        {
            var bad = Assert.IsType<BadRequestObjectResult>(result);
            return (string)bad.Value!.GetType().GetProperty("error")!.GetValue(bad.Value)!;
        }

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

    [Fact]
    public async Task The_list_offers_the_customers_admin_and_their_manager()
    {
        using var h = new Harness();
        var names = (await h.TargetsAsync()).Select(r => Harness.Read<string>(r, "fullName")).ToList();

        Assert.Contains("Musteri Admini", names);
        Assert.Contains("Filial Meneceri", names);
    }

    [Fact]
    public async Task The_list_leaves_out_the_operator_and_the_caller()
    {
        // Both are already refused by the mint; leaving them in the list would only offer a door that
        // then does not open.
        using var h = new Harness();
        var ids = (await h.TargetsAsync()).Select(r => Harness.Read<Guid>(r, "id")).ToList();

        Assert.DoesNotContain(h.OperatorId, ids);
    }

    [Fact]
    public async Task The_list_leaves_out_plain_employees()
    {
        using var h = new Harness();
        var ids = (await h.TargetsAsync()).Select(r => Harness.Read<Guid>(r, "id")).ToList();

        Assert.DoesNotContain(h.WorkerId, ids);
    }

    [Fact]
    public async Task A_managers_branches_come_back_with_them()
    {
        // Without it the list is a row of names and the operator has to guess which manager the caller
        // is describing.
        using var h = new Harness();
        var manager = (await h.TargetsAsync())
            .Single(r => Harness.Read<Guid>(r, "id") == h.ManagerId);

        Assert.Equal("Manager", Harness.Read<string>(manager, "role"));
        Assert.Equal(new[] { "Camasirxana" }, Harness.Read<List<string>>(manager, "branches"));
    }

    [Fact]
    public async Task A_chosen_manager_can_be_borrowed_and_the_answer_says_so()
    {
        using var h = new Harness();
        var ok = Assert.IsType<OkObjectResult>(await h.Controller.Impersonate(TenantId, h.ManagerId));

        Assert.Equal("Manager", Harness.Read<string>(ok.Value!, "role"));
        Assert.Equal("Filial Meneceri", Harness.Read<string>(ok.Value!, "adminName"));
    }

    [Fact]
    public async Task A_plain_employee_can_never_be_borrowed()
    {
        // The ceiling, and the reason there is one: a borrowed worker session can create attendance.
        // Support must not be able to put a day's work on somebody's record.
        using var h = new Harness();
        Assert.Equal("TargetNotImpersonable",
            Harness.ErrorOf(await h.Controller.Impersonate(TenantId, h.WorkerId)));
    }

    [Fact]
    public async Task An_operator_still_cannot_be_borrowed_even_when_named_outright()
    {
        // The 2026-08 takeover fix: a token whose sub is an allowlisted id must never be minted. Being
        // able to name a target must not become a way around it.
        using var h = new Harness();
        Assert.Equal("CannotImpersonateOperator",
            Harness.ErrorOf(await h.Controller.Impersonate(TenantId, h.OperatorId)));
    }

    [Fact]
    public async Task Somebody_from_another_company_is_not_reachable()
    {
        // The tenant boundary, on the one route that deliberately reads across tenants.
        using var h = new Harness();
        Assert.Equal("TargetNotFound",
            Harness.ErrorOf(await h.Controller.Impersonate(TenantId, h.OtherTenantAdminId)));
    }

    [Fact]
    public async Task A_deactivated_target_is_not_reachable()
    {
        using var h = new Harness();
        var manager = h.Db.Employees.Single(e => e.Id == h.ManagerId);
        manager.IsActive = false;
        h.Db.SaveChanges();

        Assert.Equal("TargetNotFound",
            Harness.ErrorOf(await h.Controller.Impersonate(TenantId, h.ManagerId)));
    }

    [Fact]
    public async Task Without_a_target_it_still_borrows_the_customers_founding_admin()
    {
        // The old behaviour is the default, so the button keeps working for every caller that has not
        // been updated — and so an operator-owned oldest admin is still skipped past.
        using var h = new Harness();
        var ok = Assert.IsType<OkObjectResult>(await h.Controller.Impersonate(TenantId));

        Assert.Equal("Musteri Admini", Harness.Read<string>(ok.Value!, "adminName"));
        Assert.Equal("Admin", Harness.Read<string>(ok.Value!, "role"));
    }
}
