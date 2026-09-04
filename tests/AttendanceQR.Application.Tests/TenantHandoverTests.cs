using System.Security.Claims;
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
/// Building a company before it has an owner, and handing it over when it is finished.
///
/// The old order was backwards. The very first field of the very first form asked for the customer
/// admin's phone number — before the company had a branch, a shift or a single employee — and it was
/// effectively one-shot, because an impersonation session is refused on every route that creates,
/// promotes or re-credentials an Admin. If the number was wrong, or the customer changed who would
/// hold the account halfway through the build, there was no way to fix it from inside the company.
///
/// Worse, the operator's natural move was to sign in with the temporary PIN the console had just
/// printed — a normal login, as that admin — which routed straight to the forced set-PIN screen and
/// then opened a camera at them and would not continue without a face. That face would have become the
/// customer admin's reference photo.
///
/// So the company is created with an admin account that belongs to NOBODY: no phone, no email, no way
/// to sign in as it. The operator configures through it by impersonation, and only at handover does
/// this endpoint give it to a real person along with their temporary PIN.
/// </summary>
public class TenantHandoverTests
{
    private static readonly Guid OperatorTenantId = Guid.Parse("00000000-0000-0000-0000-0000000000c1");

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public SuperAdminController Controller { get; }
        public Guid OperatorId { get; } = Guid.NewGuid();

        public Harness(bool asOperator = true, bool impersonating = false)
        {
            var tenant = new TenantContext();
            tenant.Resolve(OperatorTenantId);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase($"handover-{Guid.NewGuid()}").Options,
                tenant);

            Db.Tenants.Add(new Tenant
            {
                Id = OperatorTenantId, Name = "Operator", Slug = "operator",
                DisplayName = "Operator", IsActive = true,
            });
            Db.Employees.Add(new Employee
            {
                Id = OperatorId, TenantId = OperatorTenantId, FullName = "Operator Ozu",
                Role = EmployeeRole.Admin, IsActive = true, PasswordHash = "h",
                LocationId = Guid.NewGuid(),
            });
            Db.SaveChanges();

            var options = new AppOptions
            {
                SuperAdminEmployeeIds = asOperator ? OperatorId.ToString() : Guid.NewGuid().ToString(),
            };

            var claims = new List<Claim>
            {
                new("sub", OperatorId.ToString()),
                new("role", nameof(EmployeeRole.Admin)),
            };
            if (impersonating) claims.Add(new Claim("imp", OperatorId.ToString()));

            Controller = new SuperAdminController(Db, tenant, new Hasher(), new Jwt(), options)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")),
                    },
                },
            };
        }

        /// <summary>Creates a company the way the console now does: a name, and nobody to own it yet.</summary>
        public async Task<(Guid TenantId, string? TempPin)> CreateAsync(string slug = "yeni", string? adminPhone = null)
        {
            var result = await Controller.CreateTenant(new CreateTenantRequest(
                Slug: slug, DisplayName: "Yeni Sirket", AdminPhone: adminPhone));
            var ok = Assert.IsType<OkObjectResult>(result);
            return ((Guid)Prop(ok.Value!, "id")!, (string?)Prop(ok.Value!, "tempPin"));
        }

        public List<Employee> EmployeesOf(Guid tenantId) =>
            Db.Employees.IgnoreQueryFilters().AsNoTracking().Where(e => e.TenantId == tenantId).ToList();

        public void Dispose() => Db.Dispose();
    }

    private sealed class Hasher : IPasswordHasher
    {
        public string Hash(string password) => "hashed:" + password;
        public bool Verify(string hash, string password) => hash == "hashed:" + password;
    }

    private sealed class Jwt : IJwtService
    {
        public string GenerateToken(Employee employee) => "token";
        public string GenerateImpersonationToken(Employee employee, Guid impersonatedBy, int expiryMinutes, bool readOnly = false) => "imp";
    }

    private static object? Prop(object value, string name)
        => value.GetType().GetProperty(name)?.GetValue(value);

    private static string ErrorOf(IActionResult result)
    {
        var obj = Assert.IsAssignableFrom<ObjectResult>(result);
        return Prop(obj.Value!, "error")?.ToString() ?? "";
    }

    // --- creating without an owner ---------------------------------------------------------------

    [Fact]
    public async Task A_company_can_be_created_without_naming_anybody()
    {
        using var h = new Harness();
        var (tenantId, tempPin) = await h.CreateAsync();

        // No PIN was issued, because there is nobody to issue one to. The console shows "build it"
        // rather than a set of credentials to hand over.
        Assert.Null(tempPin);
        var admin = Assert.Single(h.EmployeesOf(tenantId));
        Assert.Equal(EmployeeRole.Admin, admin.Role);
        Assert.Null(admin.PhoneNumber);
        Assert.Null(admin.Email);
    }

    [Fact]
    public async Task That_admin_cannot_be_signed_in_as_by_anyone()
    {
        // The whole safety of an unclaimed account: login matches a phone or an email, and it has
        // neither, so no PIN — not even a leaked one — opens it.
        using var h = new Harness();
        var (tenantId, _) = await h.CreateAsync();

        var admin = Assert.Single(h.EmployeesOf(tenantId));
        Assert.Null(admin.PhoneNumber);
        Assert.Null(admin.Email);
        Assert.True(admin.IsActive); // still active: impersonation needs a target
    }

    [Fact]
    public async Task A_new_company_gets_its_shift_templates_immediately()
    {
        // These used to be seeded only at startup, so a company created from the console had an empty
        // shift picker until the next backend restart — during the very hour it was being set up.
        using var h = new Harness();
        var (tenantId, _) = await h.CreateAsync();

        var schedules = h.Db.Schedules.IgnoreQueryFilters().Where(s => s.TenantId == tenantId).ToList();
        Assert.Equal(2, schedules.Count);
        Assert.Contains(schedules, s => s.Name == "Gündüz");
        Assert.Contains(schedules, s => s.Name == "Gecə növbəsi");
    }

    [Fact]
    public async Task Naming_the_admin_up_front_still_works()
    {
        // The one-step path is not removed — a company whose owner is known on day one is created and
        // handed over in a single form, as before.
        using var h = new Harness();
        var (tenantId, tempPin) = await h.CreateAsync(adminPhone: "0501234567");

        Assert.False(string.IsNullOrWhiteSpace(tempPin));
        var admin = Assert.Single(h.EmployeesOf(tenantId));
        Assert.Equal("501234567", admin.PhoneNumber);
        Assert.True(admin.MustChangePin);
    }

    [Fact]
    public async Task A_phone_that_is_not_a_phone_is_still_refused()
    {
        using var h = new Harness();
        var result = await h.Controller.CreateTenant(new CreateTenantRequest(
            Slug: "yeni", DisplayName: "Yeni", AdminPhone: "salam"));
        Assert.Equal("AdminPhoneInvalid", ErrorOf(result));
    }

    // --- handing it over --------------------------------------------------------------------------

    [Fact]
    public async Task Handover_claims_the_unclaimed_account_rather_than_adding_a_second()
    {
        using var h = new Harness();
        var (tenantId, _) = await h.CreateAsync();

        var result = await h.Controller.SetTenantAdmin(tenantId,
            new SetTenantAdminRequest(Phone: "0551112233", FullName: "Musteri Admini"));
        var ok = Assert.IsType<OkObjectResult>(result);

        // One admin, not two: the company the operator built is the company the customer receives.
        var admin = Assert.Single(h.EmployeesOf(tenantId));
        Assert.Equal("Musteri Admini", admin.FullName);
        Assert.Equal("551112233", admin.PhoneNumber);
        Assert.Equal(false, Prop(ok.Value!, "created"));
    }

    [Fact]
    public async Task Handover_issues_a_temporary_PIN_and_forces_the_customer_to_replace_it()
    {
        using var h = new Harness();
        var (tenantId, _) = await h.CreateAsync();

        var result = await h.Controller.SetTenantAdmin(tenantId, new SetTenantAdminRequest(Phone: "0551112233"));
        var ok = Assert.IsType<OkObjectResult>(result);
        var pin = Prop(ok.Value!, "tempPin")?.ToString();

        Assert.False(string.IsNullOrWhiteSpace(pin));
        var admin = Assert.Single(h.EmployeesOf(tenantId));
        Assert.True(admin.MustChangePin);
        // The PIN that came back is the one that opens the account — it is shown once and never stored.
        Assert.Equal("hashed:" + pin, admin.PasswordHash);
    }

    [Fact]
    public async Task A_second_admin_is_added_beside_a_real_one_rather_than_taking_their_account()
    {
        // Once an admin belongs to a person, this endpoint must never quietly repoint their login.
        using var h = new Harness();
        var (tenantId, _) = await h.CreateAsync(adminPhone: "0501234567");

        var result = await h.Controller.SetTenantAdmin(tenantId,
            new SetTenantAdminRequest(Phone: "0559998877", FullName: "Ikinci Admin"));
        var ok = Assert.IsType<OkObjectResult>(result);

        Assert.Equal(true, Prop(ok.Value!, "created"));
        var admins = h.EmployeesOf(tenantId);
        Assert.Equal(2, admins.Count);
        Assert.Contains(admins, a => a.PhoneNumber == "501234567");
        Assert.Contains(admins, a => a.PhoneNumber == "559998877");
    }

    [Fact]
    public async Task A_number_already_used_inside_that_company_is_refused()
    {
        using var h = new Harness();
        var (tenantId, _) = await h.CreateAsync(adminPhone: "0501234567");

        var result = await h.Controller.SetTenantAdmin(tenantId, new SetTenantAdminRequest(Phone: "0501234567"));
        Assert.Equal("PhoneAlreadyExists", ErrorOf(result));
    }

    [Fact]
    public async Task The_same_number_in_a_DIFFERENT_company_is_fine()
    {
        // Phone uniqueness is per tenant. One person can be the admin of two companies — and on this
        // surface the check runs inside the target's scope, which is what makes that true.
        using var h = new Harness();
        var (first, _) = await h.CreateAsync(slug: "birinci", adminPhone: "0501234567");
        var (second, _) = await h.CreateAsync(slug: "ikinci");

        var result = await h.Controller.SetTenantAdmin(second, new SetTenantAdminRequest(Phone: "0501234567"));
        Assert.IsType<OkObjectResult>(result);
        Assert.Single(h.EmployeesOf(first));
        Assert.Single(h.EmployeesOf(second));
    }

    [Fact]
    public async Task The_new_admin_lands_in_the_target_company_and_nowhere_else()
    {
        // The reason WithTenantAsync exists. A /api/super endpoint runs as the operator, whose own
        // tenant is the operator's — writing a row without moving the scope first files the customer's
        // admin into the operator's own company, silently, with no error anywhere.
        using var h = new Harness();
        var (tenantId, _) = await h.CreateAsync();
        var operatorRowsBefore = h.EmployeesOf(OperatorTenantId).Count;

        await h.Controller.SetTenantAdmin(tenantId, new SetTenantAdminRequest(Phone: "0551112233"));

        Assert.Equal(operatorRowsBefore, h.EmployeesOf(OperatorTenantId).Count);
        Assert.Single(h.EmployeesOf(tenantId), e => e.PhoneNumber == "551112233");
    }

    [Fact]
    public async Task A_weak_PIN_is_refused_here_too()
    {
        // Same rules as everywhere else — this is a real credential going to a real customer.
        using var h = new Harness();
        var (tenantId, _) = await h.CreateAsync();

        Assert.Equal("AdminPinTooWeak", ErrorOf(await h.Controller.SetTenantAdmin(
            tenantId, new SetTenantAdminRequest(Phone: "0551112233", Pin: "1111"))));
        Assert.Equal("AdminPinInvalid", ErrorOf(await h.Controller.SetTenantAdmin(
            tenantId, new SetTenantAdminRequest(Phone: "0551112233", Pin: "abc"))));
    }

    [Fact]
    public async Task An_unknown_company_is_not_found()
    {
        using var h = new Harness();
        var result = await h.Controller.SetTenantAdmin(Guid.NewGuid(), new SetTenantAdminRequest(Phone: "0551112233"));
        Assert.Equal("TenantNotFound", ErrorOf(result));
    }

    // --- who may do it ----------------------------------------------------------------------------

    [Fact]
    public async Task Somebody_who_is_not_an_operator_cannot_create_an_admin_anywhere()
    {
        using var h = new Harness(asOperator: false);
        var result = await h.Controller.SetTenantAdmin(Guid.NewGuid(), new SetTenantAdminRequest(Phone: "0551112233"));
        var obj = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, obj.StatusCode);
    }

    [Fact]
    public async Task An_impersonation_session_cannot_reach_this_endpoint()
    {
        // The escalation this guards: an impersonation token's "sub" is the borrowed tenant admin, so
        // if that admin were themselves an operator the session would inherit the console. Minting a
        // fresh admin from inside a borrowed session is exactly the artefact the tenant-side guards
        // were written to prevent — it would outlive the hour the session lasts.
        using var h = new Harness(impersonating: true);
        var result = await h.Controller.SetTenantAdmin(Guid.NewGuid(), new SetTenantAdminRequest(Phone: "0551112233"));
        var obj = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, obj.StatusCode);
    }
}
