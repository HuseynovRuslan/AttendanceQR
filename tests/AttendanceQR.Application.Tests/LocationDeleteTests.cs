using AttendanceQR.Api.Controllers;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Deleting a branch, and the reason it kept refusing.
///
/// DailySummaries counted as history, and a summary is DERIVED: the night job writes one per employee
/// per day, absences included, filed under whichever branch that person was in at the time — so it
/// outlives their move to another branch. A branch with nobody in it and not one scan against it still
/// held rows, and the admin was told it was "in use" about a branch that demonstrably was not. It took
/// months of "why can I not delete this" to notice, because the count was invisible from the screen.
///
/// What actually protects a branch is people (deleting would orphan them) and scans (that is the
/// history). Derived rows follow the employee instead: the day and its status stay, because an absence
/// is money, and only the branch they are filed under moves.
/// </summary>
public class LocationDeleteTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000ab");

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public AdminLocationsController Controller { get; }
        public Guid OldBranch { get; } = Guid.NewGuid();
        public Guid NewBranch { get; } = Guid.NewGuid();
        public Guid EmployeeId { get; } = Guid.NewGuid();

        public Harness()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase($"loc-delete-{Guid.NewGuid()}").Options,
                tenant);

            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "T", Slug = "t", DisplayName = "T", IsActive = true });
            Db.Locations.Add(Branch(OldBranch, "kohne"));
            Db.Locations.Add(Branch(NewBranch, "yeni"));
            Db.SaveChanges();

            Controller = new AdminLocationsController(
                Db,
                new QrTokenService(Options.Create(new QrTokenOptions { Secret = "test-secret-for-location-delete", TtlSeconds = 60 })),
                new OffFaceMatch(),
                NullLogger<AdminLocationsController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            };
        }

        private static Location Branch(Guid id, string name) => new()
        {
            Id = id, TenantId = TenantId, Name = name,
            Latitude = 40.41, Longitude = 49.85, RadiusMeters = 150,
            ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
            LateThresholdMinutes = 15, QrVersion = 1, IsActive = true,
        };

        /// <summary>Somebody who used to be at the old branch and has since moved to the new one.</summary>
        public void AddMovedEmployee()
        {
            Db.Employees.Add(new Employee
            {
                Id = EmployeeId, TenantId = TenantId, FullName = "Kocmus Isci",
                Role = EmployeeRole.Employee, LocationId = NewBranch,
                IsActive = true, ActivatedAtUtc = DateTime.UtcNow, PasswordHash = "h",
            });
            Db.SaveChanges();
        }

        public void AddSummaryAtOldBranch(Guid employeeId, DailySummaryStatus status = DailySummaryStatus.Absent)
        {
            Db.DailySummaries.Add(new DailySummary
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = employeeId,
                LocationId = OldBranch, SummaryDate = new DateOnly(2026, 7, 26), Status = status,
            });
            Db.SaveChanges();
        }

        public void Dispose() => Db.Dispose();
    }

    private static (int Status, string Error, int Employees, int History) ConflictOf(IActionResult result)
    {
        var obj = Assert.IsType<ConflictObjectResult>(result);
        var v = obj.Value!;
        string s(string p) => v.GetType().GetProperty(p)?.GetValue(v)?.ToString() ?? "";
        return (obj.StatusCode ?? 0, s("error"), int.Parse(s("employeeCount")), int.Parse(s("historyCount")));
    }

    [Fact]
    public async Task An_empty_branch_deletes()
    {
        using var h = new Harness();
        Assert.IsType<OkObjectResult>(await h.Controller.Delete(h.OldBranch));
        Assert.Null(h.Db.Locations.AsNoTracking().FirstOrDefault(l => l.Id == h.OldBranch));
    }

    [Fact]
    public async Task A_branch_with_staff_is_refused_and_says_how_many()
    {
        using var h = new Harness();
        h.Db.Employees.Add(new Employee
        {
            Id = h.EmployeeId, TenantId = TenantId, FullName = "Isci", Role = EmployeeRole.Employee,
            LocationId = h.OldBranch, IsActive = true, PasswordHash = "h",
        });
        h.Db.SaveChanges();

        var (status, error, employees, _) = ConflictOf(await h.Controller.Delete(h.OldBranch));
        Assert.Equal(StatusCodes.Status409Conflict, status);
        Assert.Equal("LocationInUse", error);
        Assert.Equal(1, employees);
    }

    [Fact]
    public async Task A_branch_with_scans_against_it_is_refused()
    {
        // The real history. This one can never be deleted — it is deactivated instead.
        using var h = new Harness();
        h.AddMovedEmployee();
        h.Db.AttendanceRecords.Add(new AttendanceRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = h.EmployeeId, LocationId = h.OldBranch,
            AttendanceDate = new DateOnly(2026, 7, 26), CheckInAtUtc = DateTime.UtcNow.AddDays(-30),
            Status = AttendanceStatus.OnTime,
        });
        h.Db.SaveChanges();

        var (_, error, employees, history) = ConflictOf(await h.Controller.Delete(h.OldBranch));
        Assert.Equal("LocationInUse", error);
        Assert.Equal(0, employees);
        Assert.Equal(1, history);
    }

    [Fact]
    public async Task Derived_summaries_alone_no_longer_block_the_delete()
    {
        // The bug, in one test: nobody there, nothing scanned there, and it would not delete.
        using var h = new Harness();
        h.AddMovedEmployee();
        h.AddSummaryAtOldBranch(h.EmployeeId);

        Assert.IsType<OkObjectResult>(await h.Controller.Delete(h.OldBranch));
        Assert.Null(h.Db.Locations.AsNoTracking().FirstOrDefault(l => l.Id == h.OldBranch));
    }

    [Fact]
    public async Task The_summary_follows_the_employee_rather_than_being_thrown_away()
    {
        // An absence is money: the day and its status must survive the branch it was filed under.
        using var h = new Harness();
        h.AddMovedEmployee();
        h.AddSummaryAtOldBranch(h.EmployeeId);

        await h.Controller.Delete(h.OldBranch);

        var summary = Assert.Single(h.Db.DailySummaries.AsNoTracking().ToList());
        Assert.Equal(h.NewBranch, summary.LocationId);
        Assert.Equal(DailySummaryStatus.Absent, summary.Status);
        Assert.Equal(new DateOnly(2026, 7, 26), summary.SummaryDate);
    }

    [Fact]
    public async Task A_summary_whose_employee_is_gone_goes_with_the_branch()
    {
        // Nothing left to describe, and nowhere to move it to.
        using var h = new Harness();
        h.AddSummaryAtOldBranch(Guid.NewGuid());

        Assert.IsType<OkObjectResult>(await h.Controller.Delete(h.OldBranch));
        Assert.Empty(h.Db.DailySummaries.AsNoTracking().ToList());
    }
}
