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
/// Removing a company that never became one — a name typed wrong, a demo for a customer who did not
/// sign, a test of the creation form. There was no way to do it: every tenant-scoped table has an FK
/// to Tenant with Restrict, so the delete failed on the first employee row, and the only alternative
/// was "Söndür", which leaves the company in every list for ever.
///
/// The line this feature is built on is a single check-in. One attendance record is somebody's pay,
/// so a company with any history is refused outright — there is no force flag, because an override is
/// something an operator working alone reaches for at 18:00 on a Friday.
///
/// Two of these tests are the ones that matter. One deletes a company standing beside another and
/// proves the other is untouched, because the request runs as the operator and the tenant filter
/// therefore points at the WRONG company by default. The other pins that the sweep is driven by EF's
/// model rather than a hand-written list: TaskItem is tenant-scoped and filtered but missing from the
/// tenantScoped array in AppDbContext, so a copied list would have left its rows behind, orphaned,
/// with nothing to complain.
/// </summary>
public class TenantDeleteTests
{
    private static readonly Guid OperatorTenantId = Guid.Parse("00000000-0000-0000-0000-0000000000d1");

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public SuperAdminController Controller { get; }
        public Guid OperatorId { get; } = Guid.NewGuid();
        /// <summary>Allowlisted, with no employee row of their own — a colleague on the console.</summary>
        public Guid SecondOperatorId { get; } = Guid.NewGuid();

        public Harness(bool asOperator = true)
        {
            var tenant = new TenantContext();
            tenant.Resolve(OperatorTenantId);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase($"tenant-delete-{Guid.NewGuid()}").Options,
                tenant);

            Db.Tenants.Add(new Tenant
            {
                Id = OperatorTenantId, Name = "Operator", Slug = "operator",
                DisplayName = "Operator", IsActive = true,
            });
            Db.Employees.Add(new Employee
            {
                Id = OperatorId, TenantId = OperatorTenantId, FullName = "Operator Ozu",
                Role = EmployeeRole.Admin, IsActive = true, PasswordHash = "h", LocationId = Guid.NewGuid(),
            });
            Db.SaveChanges();

            Controller = new SuperAdminController(
                Db, tenant, new Hasher(), new Jwt(),
                new AppOptions
                {
                    SuperAdminEmployeeIds = asOperator
                        ? $"{OperatorId},{SecondOperatorId}"
                        : Guid.NewGuid().ToString(),
                })
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(
                        [
                            new Claim("sub", OperatorId.ToString()),
                            new Claim("role", nameof(EmployeeRole.Admin)),
                        ], "test")),
                    },
                },
            };
        }

        /// <summary>A company as the console makes one: a branch, two shift templates, an admin.</summary>
        public async Task<Guid> CreateAsync(string slug, string name)
        {
            var result = await Controller.CreateTenant(new CreateTenantRequest(Slug: slug, DisplayName: name));
            var ok = Assert.IsType<OkObjectResult>(result);
            return (Guid)ok.Value!.GetType().GetProperty("id")!.GetValue(ok.Value)!;
        }

        /// <summary>Switching a company off, which the delete now requires first.</summary>
        public void Suspend(Guid tenantId)
        {
            var t = Db.Tenants.First(x => x.Id == tenantId);
            t.IsActive = false;
            Db.SaveChanges();
        }

        /// <summary>The three tables a freshly created company always has rows in.</summary>
        public int RowsIn(Guid tenantId) =>
            Db.Employees.IgnoreQueryFilters().Count(e => e.TenantId == tenantId)
            + Db.Locations.IgnoreQueryFilters().Count(l => l.TenantId == tenantId)
            + Db.Schedules.IgnoreQueryFilters().Count(x => x.TenantId == tenantId);

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
        public string GenerateImpersonationToken(Employee employee, Guid by, int minutes, bool readOnly = false) => "imp";
    }

    private static string ErrorOf(IActionResult result)
    {
        var obj = Assert.IsAssignableFrom<ObjectResult>(result);
        return obj.Value?.GetType().GetProperty("error")?.GetValue(obj.Value)?.ToString() ?? "";
    }

    // --- the sweep --------------------------------------------------------------------------------

    [Fact]
    public async Task A_company_nobody_ever_used_is_deleted_with_everything_in_it()
    {
        using var h = new Harness();
        var id = await h.CreateAsync("test", "Test Sirketi");
        h.Suspend(id);
        Assert.True(h.RowsIn(id) > 0);

        var result = await h.Controller.DeleteTenant(id, new DeleteTenantRequest("Test Sirketi"));

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(0, h.RowsIn(id));
        Assert.Null(h.Db.Tenants.AsNoTracking().FirstOrDefault(t => t.Id == id));
    }

    [Fact]
    public async Task The_sweep_reaches_a_table_that_is_missing_from_the_hand_written_list()
    {
        // TaskItem is tenant-scoped and has a query filter, but is not in AppDbContext's tenantScoped
        // array — so it has no FK and no index, and nothing would have stopped its rows outliving the
        // company. This is the whole reason the purge reads EF's model instead of a copied list.
        using var h = new Harness();
        var id = await h.CreateAsync("test", "Test Sirketi");
        h.Suspend(id);
        h.Db.Tasks.Add(new TaskItem
        {
            TenantId = id, Title = "Qalıq tapşırıq", CreatedByEmployeeId = h.OperatorId,
        });
        h.Db.SaveChanges();
        Assert.Equal(1, h.Db.Tasks.IgnoreQueryFilters().Count(t => t.TenantId == id));

        await h.Controller.DeleteTenant(id, new DeleteTenantRequest("Test Sirketi"));

        Assert.Equal(0, h.Db.Tasks.IgnoreQueryFilters().Count(t => t.TenantId == id));
    }

    [Fact]
    public async Task Every_tenant_scoped_table_the_model_knows_about_is_swept()
    {
        // Not a list this test maintains either: it asks the model, and asserts nothing is left.
        using var h = new Harness();
        var id = await h.CreateAsync("test", "Test Sirketi");
        h.Suspend(id);

        await h.Controller.DeleteTenant(id, new DeleteTenantRequest("Test Sirketi"));

        var remaining = await TenantPurge.CountAsync(h.Db, id, CancellationToken.None);
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task The_company_standing_next_to_it_is_untouched()
    {
        // The request runs as the OPERATOR, whose resolved tenant is their own company — so every
        // query here has to be explicitly re-scoped. A sweep that trusted the ambient filter would
        // have deleted nothing at all, or, pointed the other way, the wrong company entirely.
        using var h = new Harness();
        var doomed = await h.CreateAsync("test", "Test Sirketi");
        h.Suspend(doomed);
        var keeper = await h.CreateAsync("real", "Real Sirket");
        var keeperRows = h.RowsIn(keeper);

        await h.Controller.DeleteTenant(doomed, new DeleteTenantRequest("Test Sirketi"));

        Assert.Equal(keeperRows, h.RowsIn(keeper));
        Assert.NotNull(h.Db.Tenants.AsNoTracking().FirstOrDefault(t => t.Id == keeper));
        // And the operator's own company, which is what the ambient filter points at.
        Assert.NotNull(h.Db.Tenants.AsNoTracking().FirstOrDefault(t => t.Id == OperatorTenantId));
        Assert.Equal(1, h.Db.Employees.IgnoreQueryFilters().Count(e => e.TenantId == OperatorTenantId));
    }

    // --- what it refuses --------------------------------------------------------------------------

    [Fact]
    public async Task One_check_in_is_enough_to_refuse()
    {
        // A scan is somebody's day of pay. There is no force flag past this point.
        using var h = new Harness();
        var id = await h.CreateAsync("test", "Test Sirketi");
        h.Suspend(id);
        var employee = h.Db.Employees.IgnoreQueryFilters().First(e => e.TenantId == id);
        h.Db.AttendanceRecords.Add(new AttendanceRecord
        {
            TenantId = id, EmployeeId = employee.Id, LocationId = employee.LocationId,
            AttendanceDate = new DateOnly(2026, 8, 24), CheckInAtUtc = DateTime.UtcNow,
            Status = AttendanceStatus.OnTime,
        });
        h.Db.SaveChanges();

        var result = await h.Controller.DeleteTenant(id, new DeleteTenantRequest("Test Sirketi"));

        Assert.Equal("TenantHasHistory", ErrorOf(result));
        Assert.NotNull(h.Db.Tenants.AsNoTracking().FirstOrDefault(t => t.Id == id));
    }

    [Fact]
    public async Task A_finished_day_counts_as_history_even_with_no_scan_behind_it()
    {
        // An absence is money too — the nightly job writes a Qayıb row for somebody who never came.
        using var h = new Harness();
        var id = await h.CreateAsync("test", "Test Sirketi");
        h.Suspend(id);
        var employee = h.Db.Employees.IgnoreQueryFilters().First(e => e.TenantId == id);
        h.Db.DailySummaries.Add(new DailySummary
        {
            TenantId = id, EmployeeId = employee.Id, LocationId = employee.LocationId,
            SummaryDate = new DateOnly(2026, 8, 24), Status = DailySummaryStatus.Absent,
        });
        h.Db.SaveChanges();

        Assert.Equal("TenantHasHistory", ErrorOf(await h.Controller.DeleteTenant(id, new DeleteTenantRequest("Test Sirketi"))));
    }

    [Fact]
    public async Task The_name_has_to_be_typed_exactly()
    {
        // The rows either side of the one the operator meant look exactly like it.
        using var h = new Harness();
        var id = await h.CreateAsync("test", "Test Sirketi");
        h.Suspend(id);

        Assert.Equal("ConfirmMismatch", ErrorOf(await h.Controller.DeleteTenant(id, new DeleteTenantRequest("test sirketi"))));
        Assert.Equal("ConfirmMismatch", ErrorOf(await h.Controller.DeleteTenant(id, new DeleteTenantRequest(""))));
        Assert.Equal("ConfirmMismatch", ErrorOf(await h.Controller.DeleteTenant(id, new DeleteTenantRequest(null))));
        Assert.NotNull(h.Db.Tenants.AsNoTracking().FirstOrDefault(t => t.Id == id));
    }

    [Fact]
    public async Task The_name_check_runs_before_anything_is_read_about_usage()
    {
        // Ordering matters for the message the operator gets: typing the wrong name must say so,
        // rather than reporting on a company they did not mean to look at.
        using var h = new Harness();
        var id = await h.CreateAsync("test", "Test Sirketi");
        var employee = h.Db.Employees.IgnoreQueryFilters().First(e => e.TenantId == id);
        h.Db.AttendanceRecords.Add(new AttendanceRecord
        {
            TenantId = id, EmployeeId = employee.Id, LocationId = employee.LocationId,
            AttendanceDate = new DateOnly(2026, 8, 24), CheckInAtUtc = DateTime.UtcNow,
            Status = AttendanceStatus.OnTime,
        });
        h.Db.SaveChanges();

        Assert.Equal("ConfirmMismatch", ErrorOf(await h.Controller.DeleteTenant(id, new DeleteTenantRequest("yanlış"))));
    }

    [Fact]
    public async Task A_running_company_has_no_delete_at_all()
    {
        // The highest-value rail, and the cheapest: switching a company off first is reversible, takes
        // a second, and puts a deliberate act between a live customer and this endpoint. The console
        // does not even show the item for a running company — this is the server saying the same.
        using var h = new Harness();
        var id = await h.CreateAsync("test", "Test Sirketi");

        Assert.Equal("TenantIsActive", ErrorOf(await h.Controller.DeleteTenant(id, new DeleteTenantRequest("Test Sirketi"))));
        Assert.NotNull(h.Db.Tenants.AsNoTracking().FirstOrDefault(t => t.Id == id));
    }

    [Fact]
    public async Task A_company_that_has_been_billed_is_a_customer_even_with_no_scans()
    {
        // And the sharper reason: TenantInvoice carries a TenantId, so the model-driven sweep would
        // have deleted the billing history along with everything else — silently, with nothing in the
        // response to say a money record had gone.
        using var h = new Harness();
        var id = await h.CreateAsync("test", "Test Sirketi");
        h.Suspend(id);
        h.Db.TenantInvoices.Add(new TenantInvoice
        {
            TenantId = id, PeriodYear = 2026, PeriodMonth = 8, EmployeeCount = 3, Amount = 12m,
        });
        h.Db.SaveChanges();

        Assert.Equal("TenantHasInvoices", ErrorOf(await h.Controller.DeleteTenant(id, new DeleteTenantRequest("Test Sirketi"))));
        Assert.Single(h.Db.TenantInvoices.AsNoTracking().Where(i => i.TenantId == id));
    }

    [Fact]
    public async Task A_company_holding_an_operator_account_is_refused()
    {
        // Deleting it would delete the operator's own employee row and lock them out of the console
        // they are standing in.
        using var h = new Harness();
        var id = await h.CreateAsync("test", "Test Sirketi");
        h.Suspend(id);
        h.Db.Employees.Add(new Employee
        {
            Id = h.SecondOperatorId, TenantId = id, FullName = "Operator (ikinci sətir)",
            Role = EmployeeRole.Admin, IsActive = true, PasswordHash = "h",
            LocationId = h.Db.Locations.IgnoreQueryFilters().First(l => l.TenantId == id).Id,
        });
        h.Db.SaveChanges();

        Assert.Equal("TenantHasOperator", ErrorOf(await h.Controller.DeleteTenant(id, new DeleteTenantRequest("Test Sirketi"))));
    }

    [Fact]
    public async Task An_unknown_company_is_not_found()
    {
        using var h = new Harness();
        Assert.Equal("TenantNotFound", ErrorOf(await h.Controller.DeleteTenant(Guid.NewGuid(), new DeleteTenantRequest("x"))));
    }

    [Fact]
    public async Task Somebody_who_is_not_an_operator_cannot_delete_anything()
    {
        using var h = new Harness(asOperator: false);
        var obj = Assert.IsAssignableFrom<ObjectResult>(
            await h.Controller.DeleteTenant(Guid.NewGuid(), new DeleteTenantRequest("x")));
        Assert.Equal(StatusCodes.Status403Forbidden, obj.StatusCode);
    }

    // --- what the operator is shown before deciding ------------------------------------------------

    [Fact]
    public async Task The_preview_says_what_would_go_and_whether_it_may()
    {
        using var h = new Harness();
        var id = await h.CreateAsync("test", "Test Sirketi");
        h.Suspend(id);

        var ok = Assert.IsType<OkObjectResult>(await h.Controller.TenantDeletable(id));
        var value = ok.Value!;
        Assert.Equal(true, value.GetType().GetProperty("canDelete")!.GetValue(value));

        var rows = (Dictionary<string, int>)value.GetType().GetProperty("rows")!.GetValue(value)!;
        Assert.Equal(1, rows["Employee"]);
        Assert.Equal(1, rows["Location"]);
        Assert.Equal(2, rows["Schedule"]);
    }

    [Fact]
    public async Task The_preview_refuses_a_company_with_history_before_the_button_is_offered()
    {
        using var h = new Harness();
        var id = await h.CreateAsync("test", "Test Sirketi");
        h.Suspend(id);
        var employee = h.Db.Employees.IgnoreQueryFilters().First(e => e.TenantId == id);
        h.Db.AttendanceRecords.Add(new AttendanceRecord
        {
            TenantId = id, EmployeeId = employee.Id, LocationId = employee.LocationId,
            AttendanceDate = new DateOnly(2026, 8, 24), CheckInAtUtc = DateTime.UtcNow,
            Status = AttendanceStatus.OnTime,
        });
        h.Db.SaveChanges();

        var ok = Assert.IsType<OkObjectResult>(await h.Controller.TenantDeletable(id));
        Assert.Equal(false, ok.Value!.GetType().GetProperty("canDelete")!.GetValue(ok.Value));
        Assert.Equal("TenantHasHistory", ok.Value!.GetType().GetProperty("reason")!.GetValue(ok.Value));
    }

    // --- what survives -----------------------------------------------------------------------------

    [Fact]
    public async Task The_record_of_the_deletion_outlives_the_company()
    {
        // SuperAdminAuditLog names its column TargetTenantId, so the sweep does not reach it. A record
        // that disappears along with what it describes is not a record.
        using var h = new Harness();
        var id = await h.CreateAsync("test", "Test Sirketi");
        h.Suspend(id);

        await h.Controller.DeleteTenant(id, new DeleteTenantRequest("Test Sirketi"));

        var audit = h.Db.SuperAdminAuditLogs.AsNoTracking().Where(a => a.Action == "TenantDeleted").ToList();
        var row = Assert.Single(audit);
        Assert.Equal("test", row.TargetTenantSlug);
        Assert.Contains("Test Sirketi", row.Details);
        Assert.Equal(h.OperatorId, row.ActorEmployeeId);
    }
}
