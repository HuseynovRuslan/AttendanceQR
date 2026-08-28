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
/// One person's card, fetched by a manager — GET /api/manager/employees/{id}.
///
/// The employee-profile screen is shared with the admin panel, and the admin version got its data by
/// pulling the WHOLE company list and picking a row out of it. That list is unscoped and it carries
/// <c>monthlySalary</c>. Handing a manager the same screen without this endpoint means either the
/// screen breaks for them or a manager reads pay for the entire company — the one thing
/// ManagerController exists to make impossible.
///
/// So two boundaries meet here and they are deliberately NOT the same width:
///   • what a manager may SEE — anyone standing at their branches, peers and admins included, which is
///     how the boards already behave and why a two-manager site stopped reading a headcount short;
///   • what a manager may CHANGE — still ManageableEmployeeAsync (plain staff, own branches, not
///     themselves), reported to the screen as <c>manageable</c> so the buttons match what the writes
///     will actually allow.
///
/// The strictest assertions below are the negative ones. <c>monthlySalary</c> and <c>role</c> are not
/// hidden by CSS, they never leave the server; the projection is a hand-written anonymous object, so
/// the way that regresses is somebody copying a field list from the admin endpoint. Reflecting over
/// the returned properties makes that copy fail here instead of shipping.
/// </summary>
public class ManagerEmployeeCardTests
{
    private static readonly Guid TenantA = Guid.Parse("00000000-0000-0000-0000-0000000000ea");
    private static readonly Guid TenantB = Guid.Parse("00000000-0000-0000-0000-0000000000eb");

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public ManagerController Controller { get; }
        public Guid MyBranch { get; } = Guid.NewGuid();     // overseen by the caller
        public Guid OtherBranch { get; } = Guid.NewGuid();  // same tenant, NOT overseen
        public Guid ManagerId { get; } = Guid.NewGuid();
        public Guid StaffId { get; } = Guid.NewGuid();
        public Guid PeerManagerId { get; } = Guid.NewGuid();
        public Guid SameBranchAdminId { get; } = Guid.NewGuid();
        public Guid OtherBranchStaffId { get; } = Guid.NewGuid();
        public Guid OtherTenantStaffId { get; } = Guid.NewGuid();

        public Harness()
        {
            // The controller runs as tenant A; the global query filter on Employees is what makes the
            // cross-tenant row below invisible, and that is fail-closed behaviour worth pinning too.
            var tenant = new TenantContext();
            tenant.Resolve(TenantA);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase($"mgr-card-{Guid.NewGuid()}").Options,
                tenant);

            Db.Tenants.Add(new Tenant { Id = TenantA, Name = "A", Slug = "a", DisplayName = "A", IsActive = true });
            Db.Tenants.Add(new Tenant { Id = TenantB, Name = "B", Slug = "b", DisplayName = "B", IsActive = true });
            Db.Locations.Add(Branch(MyBranch, "Merkez"));
            Db.Locations.Add(Branch(OtherBranch, "Novxani"));
            Db.ManagedLocations.Add(new ManagedLocation
            {
                EmployeeId = ManagerId, LocationId = MyBranch, TenantId = TenantA,
            });

            Db.Employees.Add(Person(ManagerId, "Menecer Ozu", EmployeeRole.Manager, MyBranch, TenantA));
            Db.Employees.Add(Person(StaffId, "Oz Iscim", EmployeeRole.Employee, MyBranch, TenantA));
            Db.Employees.Add(Person(PeerManagerId, "Ikinci Menecer", EmployeeRole.Manager, MyBranch, TenantA));
            Db.Employees.Add(Person(SameBranchAdminId, "Sirket Admini", EmployeeRole.Admin, MyBranch, TenantA));
            Db.Employees.Add(Person(OtherBranchStaffId, "Basqa Filial Iscisi", EmployeeRole.Employee, OtherBranch, TenantA));
            // Deliberately parked at the manager's OWN branch id: nothing but the tenant filter keeps
            // this row out of the response, which is the point of the assertion that uses it.
            Db.Employees.Add(Person(OtherTenantStaffId, "Basqa Tenant Iscisi", EmployeeRole.Employee, MyBranch, TenantB));
            Db.SaveChanges();

            Controller = new ManagerController(Db, new StubHasher(), new StubSummary(), new AppOptions())
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                        {
                            new Claim("sub", ManagerId.ToString()),
                            new Claim("role", nameof(EmployeeRole.Manager)),
                        }, "test")),
                    },
                },
            };
        }

        private static Location Branch(Guid id, string name) => new()
        {
            Id = id, TenantId = TenantA, Name = name,
            Latitude = 40.4, Longitude = 49.8, RadiusMeters = 150,
            ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
            LateThresholdMinutes = 15, QrVersion = 1, IsActive = true,
        };

        private static Employee Person(Guid id, string name, EmployeeRole role, Guid location, Guid tenantId) => new()
        {
            Id = id, TenantId = tenantId, FullName = name, Role = role, LocationId = location,
            IsActive = true, PasswordHash = "h", PhoneNumber = "+994500000000", Email = "x@example.com", ActivatedAtUtc = DateTime.UtcNow,
            Position = "Suruculuk",
            // Real identifiers on every fixture. They were all null, which is why the first draft of
            // this endpoint shipped a peer's telephone number and no test saw it.
            MonthlySalary = 850m,
        };

        /// <summary>The card as the screen receives it, or null when the endpoint refused.</summary>
        public async Task<object?> CardAsync(Guid id)
        {
            var result = await Controller.Employee(id);
            return result is OkObjectResult ok ? ok.Value : null;
        }

        public async Task<IActionResult> RawAsync(Guid id) => await Controller.Employee(id);

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

    private static IReadOnlyList<string> PropertyNames(object card) =>
        card.GetType().GetProperties().Select(p => p.Name).ToList();

    private static object? Value(object card, string property) =>
        card.GetType().GetProperty(property)?.GetValue(card);

    // --- who the card opens for (the SEE boundary) --------------------------------

    [Fact]
    public async Task A_manager_opens_their_own_branch_staff()
    {
        using var h = new Harness();

        var card = await h.CardAsync(h.StaffId);

        Assert.NotNull(card);
        Assert.Equal(h.StaffId, Value(card!, "id"));
        Assert.Equal("Oz Iscim", Value(card!, "fullName"));
        // The branch NAME is why this endpoint reads Locations at all — the screen shows it, and a
        // manager cannot fetch the location list to resolve the id themselves.
        Assert.Equal("Merkez", Value(card!, "locationName"));
    }

    [Fact]
    public async Task A_manager_opens_their_own_card()
    {
        // Their own profile row is reached through the same screen. A manager whose branch assignment
        // is later cleared would otherwise be locked out of their own details.
        using var h = new Harness();

        var card = await h.CardAsync(h.ManagerId);

        Assert.NotNull(card);
        Assert.Equal(true, Value(card!, "isSelf"));
    }

    [Fact]
    public async Task A_manager_opens_a_peer_manager_standing_at_their_branch()
    {
        // Wider than the ACT rule on purpose. The boards already count and list this person; a card
        // that 404s for a name the manager is looking at reads as the app being broken, and a
        // two-manager site spent a release short of a head for exactly that reason.
        using var h = new Harness();

        var card = await h.CardAsync(h.PeerManagerId);

        Assert.NotNull(card);
        Assert.Equal("Ikinci Menecer", Value(card!, "fullName"));
    }

    [Fact]
    public async Task A_manager_opens_an_admin_who_clocks_in_at_their_branch()
    {
        // Same widening, and the row that makes people nervous: an Admin carries a LocationId because
        // they scan too. Seeing their name and shift is fine — see ManagerAccountScopeTests for the
        // half that has not moved, where every write against this person is refused.
        using var h = new Harness();

        var card = await h.CardAsync(h.SameBranchAdminId);

        Assert.NotNull(card);
        Assert.Equal("Sirket Admini", Value(card!, "fullName"));
    }

    [Fact]
    public async Task Someone_at_another_branch_is_not_found()
    {
        // Not 403: a distinguishable refusal would let a manager walk employee ids and learn who
        // exists at branches that are none of their business.
        using var h = new Harness();

        Assert.IsType<NotFoundObjectResult>(await h.RawAsync(h.OtherBranchStaffId));
    }

    [Fact]
    public async Task Someone_in_another_tenant_is_not_found()
    {
        // Even sitting on the very location id this manager oversees. If this ever returns a card, the
        // tenant query filter has stopped covering the endpoint and the leak is cross-company.
        using var h = new Harness();

        Assert.IsType<NotFoundObjectResult>(await h.RawAsync(h.OtherTenantStaffId));
    }

    // --- what the card may never carry -------------------------------------------

    [Fact]
    public async Task The_card_carries_no_salary_field_of_any_name()
    {
        // The whole reason this endpoint was written instead of reusing the admin list. Matched on the
        // substring rather than the exact name so a future "salaryGross" or "baseSalary" is caught too.
        using var h = new Harness();

        var card = await h.CardAsync(h.StaffId);

        Assert.NotNull(card);
        Assert.DoesNotContain(
            PropertyNames(card!),
            name => name.Contains("salary", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_card_carries_no_role_field()
    {
        // A manager may not promote anyone, so shipping the role invites a screen to render a role
        // picker whose save will be refused — and it tells a manager which colleagues outrank them,
        // which is the reconnaissance half of the 2026-08-08 takeover.
        using var h = new Harness();

        var card = await h.CardAsync(h.SameBranchAdminId);

        Assert.NotNull(card);
        Assert.DoesNotContain(
            PropertyNames(card!),
            name => string.Equals(name, "role", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_card_still_carries_what_the_screen_draws()
    {
        // An anchor for the two assertions above: if the projection were replaced wholesale by some
        // other object, "no salary, no role" would pass while the screen showed nothing. These are the
        // fields the profile page actually reads.
        using var h = new Harness();

        var card = await h.CardAsync(h.StaffId);

        Assert.NotNull(card);
        var names = PropertyNames(card!);
        foreach (var expected in new[]
                 {
                     "id", "isSelf", "fullName", "position", "phoneNumber", "email",
                     "locationId", "locationName", "workStart", "workEnd", "scheduleId",
                     "isActive", "activated", "manageable",
                 })
            Assert.Contains(expected, names);
    }

    // --- manageable: what the buttons are allowed to be ---------------------------

    [Fact]
    public async Task Plain_staff_at_a_managed_branch_are_manageable()
    {
        using var h = new Harness();

        var card = await h.CardAsync(h.StaffId);

        Assert.Equal(true, Value(card!, "manageable"));
    }

    [Fact]
    public async Task A_peer_manager_is_visible_but_not_manageable()
    {
        // The seam this whole file exists to hold open: the card loads, and the edit and reset-PIN
        // buttons must not. If this flips to true the screen offers actions the server will 403 —
        // and, worse, invites someone to look for a way to make them stick.
        using var h = new Harness();

        var card = await h.CardAsync(h.PeerManagerId);

        Assert.NotNull(card);
        Assert.Equal(false, Value(card!, "manageable"));
    }

    [Fact]
    public async Task A_same_branch_admin_is_visible_but_not_manageable()
    {
        using var h = new Harness();

        var card = await h.CardAsync(h.SameBranchAdminId);

        Assert.NotNull(card);
        Assert.Equal(false, Value(card!, "manageable"));
    }

    [Fact]
    public async Task A_manager_is_not_manageable_by_themselves()
    {
        // Self-service here would mean a manager editing their own branch or reissuing their own PIN
        // with no admin in the loop — the account boundary answered from inside the account.
        using var h = new Harness();

        var card = await h.CardAsync(h.ManagerId);

        Assert.NotNull(card);
        Assert.Equal(false, Value(card!, "manageable"));
    }

    [Fact]
    public async Task A_peers_login_identifiers_are_not_on_their_card()
    {
        // Login is a phone number or an email plus four digits. Handing a manager the telephone number
        // of the admin who clocks in at their branch hands them half that admin's credentials — the
        // reconnaissance half of the 2026-08-08 takeover, and the reason `role` is withheld too.
        //
        // Every fixture in this file left PhoneNumber null until now, which is exactly why the first
        // draft of the endpoint shipped the leak and no test noticed.
        using var h = new Harness();

        foreach (var id in new[] { h.PeerManagerId, h.SameBranchAdminId })
        {
            var card = await h.CardAsync(id);
            Assert.Null(Value(card!, "phoneNumber"));
            Assert.Null(Value(card!, "email"));
            // Identity still comes through — the card is openable, it just is not an account page.
            Assert.NotNull(Value(card!, "fullName"));
        }
    }

    [Fact]
    public async Task Their_own_staffs_details_are_still_there()
    {
        // The other half. Redacting everyone would have made the card useless for the people it is
        // for — a manager rings their own worker.
        using var h = new Harness();

        var card = await h.CardAsync(h.StaffId);
        Assert.Equal("+994500000000", Value(card!, "phoneNumber"));
    }
}
