using System.Security.Claims;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Application.Common;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Security;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// An account kept out of the lists can still be somebody who comes to work.
///
/// HiddenEmails kept the operators' own accounts out of the staff roster AND out of every report. One such
/// account is a real person clocking in at a branch every day, and the managers of that branch could not see
/// that they came. HiddenEmailsCountedInReports lets a hidden account be counted in attendance — the board, the
/// tabel, hours, the nightly summaries — while it stays exactly as hidden in the lists, where an admin's edit
/// form is one click away.
///
/// Pinned: the reports count it only when listed; the lists hide it either way; a manager still may not manage
/// it; and the two sides of midnight (live reports, nightly summaries) agree.
/// </summary>
public class HiddenEmailsCountedInReportsTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000c3");
    private const string OperatorEmail = "op-account@baki.local";
    private const string RootEmail = "root@qrlog.az";

    private sealed class World : IDisposable
    {
        public AppDbContext Db { get; }
        public AppOptions Options { get; }
        public Guid Branch { get; }
        public Guid OtherBranch { get; }
        public Guid ManagerId { get; } = Guid.NewGuid();
        public Guid HiddenAdminId { get; } = Guid.NewGuid();
        public Guid RootId { get; } = Guid.NewGuid();
        public Guid StaffId { get; } = Guid.NewGuid();
        private readonly TimeZoneInfo _baku = TimeZoneInfo.FindSystemTimeZoneById("Asia/Baku");

        public World(bool counted)
        {
            Options = new AppOptions
            {
                TimeZone = "Asia/Baku",
                HiddenEmails = $"{RootEmail}, {OperatorEmail}",
                // Written the way a person types it into .env: spaces, another case.
                HiddenEmailsCountedInReports = counted ? " OP-Account@Baki.local " : "",
            };
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"hidden-counted-{Guid.NewGuid()}").Options,
                tenant);
            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "T", Slug = "t", DisplayName = "T", IsActive = true });
            Branch = AddBranch("Ümumi");
            OtherBranch = AddBranch("Başqa");

            Db.Employees.Add(Person(ManagerId, "Menecer", EmployeeRole.Manager, Branch, email: null));
            Db.ManagedLocations.Add(new ManagedLocation { EmployeeId = ManagerId, LocationId = Branch, TenantId = TenantId });
            Db.Employees.Add(Person(HiddenAdminId, "Operatorun hesabı", EmployeeRole.Admin, Branch, OperatorEmail));
            Db.Employees.Add(Person(RootId, "Root", EmployeeRole.Admin, Branch, RootEmail));
            Db.Employees.Add(Person(StaffId, "Adi İşçi", EmployeeRole.Employee, Branch, email: null));

            // The hidden admin worked yesterday and is in today; the root account never clocks in.
            var today = Today();
            Db.AttendanceRecords.Add(Record(HiddenAdminId, today.AddDays(-1), 9, 18));
            Db.AttendanceRecords.Add(Record(StaffId, today.AddDays(-1), 9, 18));
            Db.AttendanceRecords.Add(Record(HiddenAdminId, today, 0, null));
            Db.SaveChanges();
        }

        public DateOnly Today() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _baku));

        private Guid AddBranch(string name)
        {
            var l = new Location
            {
                TenantId = TenantId, Name = name, Latitude = 40.4, Longitude = 49.8, RadiusMeters = 150,
                ShiftStart = new TimeOnly(0, 0), ShiftEnd = new TimeOnly(23, 59), WorkDaysMask = 127, IsActive = true,
            };
            Db.Locations.Add(l);
            return l.Id;
        }

        private static Employee Person(Guid id, string name, EmployeeRole role, Guid branch, string? email) => new()
        {
            Id = id, TenantId = TenantId, FullName = name, Role = role, IsActive = true, PasswordHash = "h",
            LocationId = branch, ActivatedAtUtc = DateTime.UtcNow.AddDays(-30), Email = email,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-30),
        };

        private AttendanceRecord Record(Guid employee, DateOnly day, int inHourLocal, int? outHourLocal)
        {
            DateTime Utc(int hour) => TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(new TimeOnly(hour, 0)), _baku);
            // Today's check-in is a few minutes ago, so it is never in the future whenever the test runs.
            var checkIn = day == Today() ? DateTime.UtcNow.AddMinutes(-5) : Utc(inHourLocal);
            return new AttendanceRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = employee, LocationId = Branch,
                AttendanceDate = day, CheckInAtUtc = checkIn,
                CheckOutAtUtc = outHourLocal is int h ? Utc(h) : null, Status = AttendanceStatus.OnTime,
            };
        }

        public ReportQueryService Reports() => new(Db, Options);

        public AdminController Admin() => new(
            Db, Microsoft.Extensions.Options.Options.Create(new InvitationOptions()), new PasswordHasher(),
            new MemoryCacheLoginLockoutStore(new MemoryCache(new MemoryCacheOptions())), Options, new NoPhotos(),
            NullLogger<AdminController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim("sub", Guid.NewGuid().ToString()),
                        new Claim("role", nameof(EmployeeRole.Admin)),
                    }, "test")),
                },
            },
        };

        public void Dispose() => Db.Dispose();
    }

    private sealed class NoPhotos : IPhotoStorageService
    {
        public Task<string> UploadCheckInPhotoAsync(Guid e, Guid r, byte[] b, CancellationToken ct = default) => Task.FromResult("k");
        public Task<string> UploadReferencePhotoAsync(Guid e, byte[] b, CancellationToken ct = default) => Task.FromResult("k");
        public Task<string> UploadAvatarAsync(Guid e, byte[] b, CancellationToken ct = default) => Task.FromResult("k");
        public Task<string> UploadFieldWorkPhotoAsync(Guid t, Guid v, byte[] b, CancellationToken ct = default) => Task.FromResult("k");
        public Task<string> GetPresignedUrlAsync(string key, CancellationToken ct = default) => Task.FromResult(key);
        public Task<byte[]> GetBytesAsync(string key, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
        public Task DeleteByPrefixOlderThanAsync(string p, DateTime o, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> DeleteObjectsAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default) => Task.FromResult(keys.Count);
    }

    private static List<Guid> ListedIds(IActionResult result)
    {
        var value = Assert.IsType<OkObjectResult>(result).Value!;
        var items = (System.Collections.IEnumerable)value.GetType().GetProperty("items")!.GetValue(value)!;
        return items.Cast<object>().Select(i => (Guid)i.GetType().GetProperty("id")!.GetValue(i)!).ToList();
    }

    // --- the option itself ---------------------------------------------------------------

    [Fact]
    public void Reports_leave_out_the_hidden_accounts_minus_the_counted_ones()
    {
        var options = new AppOptions
        {
            HiddenEmails = "root@qrlog.az, Op-Account@baki.local",
            HiddenEmailsCountedInReports = " op-account@BAKI.local , not-hidden@example.az",
        };

        Assert.Equal(new[] { "root@qrlog.az", "op-account@baki.local" }, options.HiddenEmailList());
        // Only the root account stays out of reports; an address that was never hidden adds nothing.
        Assert.Equal(new[] { "root@qrlog.az" }, options.ReportHiddenEmailList());
        Assert.Equal(options.HiddenEmailList(), new AppOptions { HiddenEmails = options.HiddenEmails }.ReportHiddenEmailList());
    }

    // --- the reports ---------------------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_branch_manager_sees_a_counted_hidden_account_on_the_board_and_in_the_tabel(bool counted)
    {
        using var w = new World(counted);
        var today = w.Today();

        var board = await w.Reports().GetTodayAttendanceAsync(w.ManagerId, EmployeeRole.Manager);
        var (access, tabel) = await w.Reports().GetTabelAsync(today.Year, today.Month, null, w.ManagerId, EmployeeRole.Manager);

        Assert.Equal(ReportAccess.Allowed, access);
        Assert.Equal(counted, board.Any(r => r.EmployeeId == w.HiddenAdminId));
        Assert.Equal(counted, tabel!.Rows.Any(r => r.EmployeeId == w.HiddenAdminId));
        // The root account is never counted, and plain staff always are.
        Assert.DoesNotContain(board, r => r.EmployeeId == w.RootId);
        Assert.DoesNotContain(tabel.Rows, r => r.EmployeeId == w.RootId);
        Assert.Contains(board, r => r.EmployeeId == w.StaffId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_nightly_summary_agrees_with_the_live_reports(bool counted)
    {
        using var w = new World(counted);
        var yesterday = w.Today().AddDays(-1);

        await new DailySummaryService(w.Db, w.Options).GenerateForDateAsync(yesterday);

        var summarised = w.Db.DailySummaries.AsNoTracking().Where(s => s.SummaryDate == yesterday).Select(s => s.EmployeeId).ToList();
        Assert.Equal(counted, summarised.Contains(w.HiddenAdminId));
        Assert.DoesNotContain(w.RootId, summarised);
        Assert.Contains(w.StaffId, summarised);
    }

    // --- what does not move --------------------------------------------------------------

    [Fact]
    public async Task The_staff_list_hides_the_account_either_way()
    {
        using var w = new World(counted: true);

        var listed = ListedIds(await w.Admin().List());

        Assert.DoesNotContain(w.HiddenAdminId, listed);
        Assert.DoesNotContain(w.RootId, listed);
        Assert.Contains(w.StaffId, listed);
    }

    [Fact]
    public async Task Seeing_it_does_not_let_the_manager_manage_it()
    {
        using var w = new World(counted: true);

        Assert.False(await LocationScopeRules.CanManageEmployeeAsync(w.Db, w.ManagerId, EmployeeRole.Manager, w.HiddenAdminId, default));
        Assert.True(await LocationScopeRules.CanManageEmployeeAsync(w.Db, w.ManagerId, EmployeeRole.Manager, w.StaffId, default));
    }
}
