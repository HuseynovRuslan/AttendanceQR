using System.Reflection;
using System.Security.Claims;
using System.Threading.Channels;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Application.Common;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Unclosed days are now a branch manager's job too, and this is the line that job stops at.
///
/// A day with a check-in and no check-out scores ZERO hours — the person worked and their month says
/// they did not. Only the branch knows which of those is a forgotten scan and which is somebody who
/// went home early, so leaving the whole screen with the admin meant a manager watching their own
/// people's time disappear with nothing they could do about it. Hence the widening.
///
/// What must not travel with it is reach over accounts that are not theirs. A manager may act only on
/// a Role==Employee target inside a branch they oversee — the 2026-08-08 rule that stopped a manager
/// reaching a same-branch ADMIN, who carries a LocationId like everybody else because they clock in
/// too. Closing an admin's day is not a takeover on its own, but it writes a pay-critical time onto an
/// account above the manager's, and the point of that rule is that branch membership alone never
/// grants that.
///
/// The reflection tests at the bottom guard the other half. The class attribute now admits Managers,
/// so the two actions that must stay with the admin depend entirely on their own attributes — and an
/// attribute is exactly the kind of thing dropped in a later edit with nothing else noticing.
/// </summary>
public class OpenRecordsManagerScopeTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000e8");

    /// <summary>The UTC day — the same one Open() compares AttendanceDate against.</summary>
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static DateTime At(DateOnly date, int hour) =>
        DateTime.SpecifyKind(date.ToDateTime(new TimeOnly(hour, 0)), DateTimeKind.Utc);

    private sealed class Fixture : IDisposable
    {
        public AppDbContext Db { get; }

        public Guid ManagedBranch { get; } = Guid.NewGuid();
        public Guid OtherBranch { get; } = Guid.NewGuid();

        public Guid ManagerId { get; } = Guid.NewGuid();
        public Guid AdminId { get; } = Guid.NewGuid();
        public Guid MyWorkerId { get; } = Guid.NewGuid();
        public Guid TheirWorkerId { get; } = Guid.NewGuid();

        /// <summary>An admin who clocks in at the branch our manager oversees — the P0 row.</summary>
        public Guid SameBranchAdminId { get; } = Guid.NewGuid();

        /// <summary>A second manager at the same branch — Heydər Əliyev Mərkəzi has four.</summary>
        public Guid PeerManagerId { get; } = Guid.NewGuid();

        // Unclosed days (check-in, no check-out) on a PAST date — what the screen lists.
        public Guid OpenMineId { get; } = Guid.NewGuid();
        public Guid OpenTheirsId { get; } = Guid.NewGuid();
        public Guid OpenAdminId { get; } = Guid.NewGuid();
        public Guid OpenPeerId { get; } = Guid.NewGuid();

        /// <summary>The manager's OWN forgotten day — theirs to see, never theirs to close.</summary>
        public Guid OpenSelfId { get; } = Guid.NewGuid();

        /// <summary>Open, but dated today: somebody still at work, not a forgotten scan.</summary>
        public Guid OpenTodayMineId { get; } = Guid.NewGuid();

        // Closed days, for the undo-a-check-out action.
        public Guid ClosedMineId { get; } = Guid.NewGuid();
        public Guid ClosedTheirsId { get; } = Guid.NewGuid();
        public Guid ClosedAdminId { get; } = Guid.NewGuid();

        public static readonly DateOnly OpenDate = Today.AddDays(-3);
        public static readonly DateOnly ClosedDate = Today.AddDays(-5);

        public Fixture()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase($"open-records-{Guid.NewGuid()}").Options,
                tenant);

            Db.Tenants.Add(new Tenant
            {
                Id = TenantId, Name = "T", Slug = "t", DisplayName = "T", IsActive = true,
            });
            Db.Locations.Add(Branch(ManagedBranch, "Menim filialim"));
            Db.Locations.Add(Branch(OtherBranch, "Basqa filial"));

            // Scope comes from ManagedLocations, never from the manager's own LocationId.
            Db.ManagedLocations.Add(new ManagedLocation
            {
                EmployeeId = ManagerId, LocationId = ManagedBranch, TenantId = TenantId,
            });

            Db.Employees.Add(Person(ManagerId, "Menecer", EmployeeRole.Manager, ManagedBranch));
            Db.Employees.Add(Person(AdminId, "Sirket Admini", EmployeeRole.Admin, ManagedBranch));
            Db.Employees.Add(Person(MyWorkerId, "Menim Iscim", EmployeeRole.Employee, ManagedBranch));
            Db.Employees.Add(Person(TheirWorkerId, "Ozge Isci", EmployeeRole.Employee, OtherBranch));
            Db.Employees.Add(Person(SameBranchAdminId, "Eyni Filialda Admin", EmployeeRole.Admin, ManagedBranch));
            Db.Employees.Add(Person(PeerManagerId, "Hemkar Menecer", EmployeeRole.Manager, ManagedBranch));

            Db.AttendanceRecords.Add(Record(OpenMineId, MyWorkerId, ManagedBranch, OpenDate, closed: false));
            Db.AttendanceRecords.Add(Record(OpenTheirsId, TheirWorkerId, OtherBranch, OpenDate, closed: false));
            Db.AttendanceRecords.Add(Record(OpenAdminId, SameBranchAdminId, ManagedBranch, OpenDate, closed: false));
            Db.AttendanceRecords.Add(Record(OpenPeerId, PeerManagerId, ManagedBranch, OpenDate, closed: false));
            Db.AttendanceRecords.Add(Record(OpenSelfId, ManagerId, ManagedBranch, OpenDate, closed: false));
            Db.AttendanceRecords.Add(Record(OpenTodayMineId, MyWorkerId, ManagedBranch, Today, closed: false));
            Db.AttendanceRecords.Add(Record(ClosedMineId, MyWorkerId, ManagedBranch, ClosedDate, closed: true));
            Db.AttendanceRecords.Add(Record(ClosedTheirsId, TheirWorkerId, OtherBranch, ClosedDate, closed: true));
            Db.AttendanceRecords.Add(Record(ClosedAdminId, SameBranchAdminId, ManagedBranch, ClosedDate, closed: true));

            Db.SaveChanges();
        }

        private static Location Branch(Guid id, string name) => new()
        {
            Id = id, TenantId = TenantId, Name = name,
            Latitude = 40.4093, Longitude = 49.8671, RadiusMeters = 150,
            ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
            LateThresholdMinutes = 15, QrVersion = 1, IsActive = true,
        };

        private static Employee Person(Guid id, string name, EmployeeRole role, Guid locationId) => new()
        {
            Id = id, TenantId = TenantId, FullName = name, Role = role, LocationId = locationId,
            IsActive = true, PasswordHash = "h", ActivatedAtUtc = DateTime.UtcNow,
        };

        private static AttendanceRecord Record(
            Guid id, Guid employeeId, Guid locationId, DateOnly date, bool closed) => new()
        {
            Id = id, TenantId = TenantId, EmployeeId = employeeId, LocationId = locationId,
            AttendanceDate = date, CheckInAtUtc = At(date, 9),
            CheckOutAtUtc = closed ? At(date, 18) : null,
            Status = closed ? AttendanceStatus.OnTime : AttendanceStatus.Incomplete,
        };

        public AdminAttendanceController As(Guid who, EmployeeRole role) =>
            // The photo store and logger arrived with the record-DELETE endpoint; neither is reached by
            // anything this file exercises. See RecordDeleteScopeTests for the ones that do.
            new(Db, new StubSummary(), new StubQueue(), new NoPhotos(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<AdminAttendanceController>.Instance,
                new AppOptions { TimeZone = "Asia/Baku" })
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                        {
                            new Claim("sub", who.ToString()),
                            new Claim("role", role.ToString()),
                        }, "test")),
                    },
                },
            };

        public AdminAttendanceController AsManager() => As(ManagerId, EmployeeRole.Manager);

        public AdminAttendanceController AsAdmin() => As(AdminId, EmployeeRole.Admin);

        /// <summary>The stored row, read past the change tracker so a refused write cannot look applied.</summary>
        public AttendanceRecord Row(Guid recordId) =>
            Db.AttendanceRecords.AsNoTracking().Single(r => r.Id == recordId);

        public int AuditRows() => Db.AuditLogs.AsNoTracking().Count();

        public void Dispose() => Db.Dispose();
    }

    private sealed class NoPhotos : AttendanceQR.Infrastructure.Services.IPhotoStorageService
    {
        public Task<string> UploadCheckInPhotoAsync(Guid e, Guid r, byte[] b, CancellationToken ct = default) => Task.FromResult("k");
        public Task<string> UploadReferencePhotoAsync(Guid e, byte[] b, CancellationToken ct = default) => Task.FromResult("k");
        public Task<string> UploadAvatarAsync(Guid e, byte[] b, CancellationToken ct = default) => Task.FromResult("k");
        public Task<string> UploadFieldWorkPhotoAsync(Guid t, Guid v, byte[] b, CancellationToken ct = default) => Task.FromResult("k");
        public Task<string> GetPresignedUrlAsync(string key, CancellationToken ct = default) => Task.FromResult("url");
        public Task<byte[]> GetBytesAsync(string key, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
        public Task DeleteByPrefixOlderThanAsync(string p, DateTime o, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> DeleteObjectsAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default) => Task.FromResult(keys.Count);
    }

    private sealed class StubSummary : IDailySummaryService
    {
        public Task<int> GenerateForDateAsync(DateOnly date, CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class StubQueue : IFaceMatchQueue
    {
        public void Enqueue(Guid tenantId, Guid recordId) { }
        public ChannelReader<FaceMatchJob> Reader => Channel.CreateUnbounded<FaceMatchJob>().Reader;
    }

    private static List<Guid> ListedRecordIds(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return ((System.Collections.IEnumerable)ok.Value!).Cast<object>()
            .Select(r => (Guid)r.GetType().GetProperty("recordId")!.GetValue(r)!)
            .ToList();
    }

    private static void AssertOutOfScope(IActionResult result) => AssertRefused(result, "OutOfScope");

    private static void AssertRefused(IActionResult result, string error)
    {
        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, obj.StatusCode);
        // The screen switches on this code to explain the refusal instead of showing a bare failure.
        Assert.Equal(error, obj.Value!.GetType().GetProperty("error")!.GetValue(obj.Value));
    }

    /// <summary>Closing a day the way the screen does: fill in the missing departure time.</summary>
    private static AdminAttendanceUpdateRequest CloseAt(DateOnly date) => new(null, At(date, 18), "Çıxış vurmağı unudub");

    private static readonly AttendanceCorrectionRequest Why = new("Təkrar skan — səhv çıxış");

    // --- the list ---------------------------------------------------------------------------------

    [Fact]
    public async Task A_managers_list_is_their_own_branches_and_stops_there()
    {
        // The list is capped at 500 rows. A manager shown the whole company would not merely see too
        // much: a bigger branch's backlog would push their own people off the end of it, which is the
        // one thing this screen exists to prevent.
        using var f = new Fixture();

        var ids = ListedRecordIds(await f.AsManager().Open());

        Assert.Contains(f.OpenMineId, ids);
        Assert.DoesNotContain(f.OpenTheirsId, ids);
    }

    [Fact]
    public async Task An_admin_still_sees_the_whole_company()
    {
        // Narrowing the manager must not have narrowed the admin — they are the only one who can reach
        // a branch whose manager is unassigned, and three CleanFix branches still are.
        using var f = new Fixture();

        var ids = ListedRecordIds(await f.AsAdmin().Open());

        Assert.Contains(f.OpenMineId, ids);
        Assert.Contains(f.OpenTheirsId, ids);
        Assert.Contains(f.OpenAdminId, ids);
    }

    [Fact]
    public async Task Todays_open_day_is_offered_to_nobody()
    {
        // An open record dated today is somebody still at work. Listing it invites a manager to "close"
        // a shift that has not finished, stamping a departure time the employee never had.
        using var f = new Fixture();

        Assert.DoesNotContain(f.OpenTodayMineId, ListedRecordIds(await f.AsManager().Open()));
        Assert.DoesNotContain(f.OpenTodayMineId, ListedRecordIds(await f.AsAdmin().Open()));
    }

    [Fact]
    public async Task Seeing_a_row_is_not_being_able_to_close_it()
    {
        // The seam between the two rules, pinned in one place. The list is filtered by BRANCH, so a
        // same-branch admin's unclosed day is visible to their manager — the same information they get
        // from standing at the gate. Acting on it is the stricter question, and the answer is no.
        using var f = new Fixture();

        Assert.Contains(f.OpenAdminId, ListedRecordIds(await f.AsManager().Open()));
        AssertOutOfScope(await f.AsManager().Update(f.OpenAdminId, CloseAt(Fixture.OpenDate)));
    }

    private static Dictionary<Guid, bool> Closable(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return ((System.Collections.IEnumerable)ok.Value!).Cast<object>().ToDictionary(
            r => (Guid)r.GetType().GetProperty("recordId")!.GetValue(r)!,
            r => (bool)r.GetType().GetProperty("closable")!.GetValue(r)!);
    }

    [Fact]
    public async Task Each_listed_day_says_whether_this_manager_may_close_it()
    {
        // Alıyev Nihat, 2026-09-14: «çıxışı redaktə edə bilmirəm». His list showed his own forgotten day and
        // a fellow manager's three; each took a time and answered «Bağlanmadı» with no reason. The server
        // was refusing them correctly all along — the list just never said which rows those were.
        using var f = new Fixture();

        var closable = Closable(await f.AsManager().Open());

        Assert.True(closable[f.OpenMineId]);
        Assert.False(closable[f.OpenAdminId]);
        // A fellow manager's day IS closable since 2026-09-18 — exactly what Nihat was asking for.
        Assert.True(closable[f.OpenPeerId]);
        Assert.False(closable[f.OpenSelfId]);

        // The flag is the write's own answer, not a second opinion that could drift away from it. One's own
        // day says so by name since 2026-10-15: nobody corrects the day they are paid for.
        AssertRefused(await f.AsManager().Update(f.OpenSelfId, CloseAt(Fixture.OpenDate)), "OwnAttendance");
    }

    [Fact]
    public async Task An_admin_may_close_every_day_on_their_list()
    {
        using var f = new Fixture();

        Assert.All(Closable(await f.AsAdmin().Open()).Values, v => Assert.True(v));
    }

    // --- closing a day (PUT) ----------------------------------------------------------------------

    [Fact]
    public async Task A_manager_closes_their_own_staffs_day()
    {
        // The reason the screen was widened at all: this record scored zero hours until somebody who
        // knew what happened could put the departure time on it.
        using var f = new Fixture();

        Assert.IsType<OkObjectResult>(await f.AsManager().Update(f.OpenMineId, CloseAt(Fixture.OpenDate)));

        Assert.Equal(At(Fixture.OpenDate, 18), f.Row(f.OpenMineId).CheckOutAtUtc);
        // Typed by hand, not scanned — a pay-critical time must never be indistinguishable from a scan.
        Assert.Equal(f.ManagerId, f.Row(f.OpenMineId).ManualByEmployeeId);
        Assert.Equal(1, f.AuditRows());
    }

    [Fact]
    public async Task A_manager_cannot_write_a_time_onto_a_same_branch_admins_day()
    {
        // The 2026-08-08 boundary, in the place it is easiest to lose: an Admin has a LocationId
        // because they clock in, so a branch-only check would make them a perfectly legal target.
        using var f = new Fixture();

        AssertOutOfScope(await f.AsManager().Update(f.OpenAdminId, CloseAt(Fixture.OpenDate)));

        Assert.Null(f.Row(f.OpenAdminId).CheckOutAtUtc);
        Assert.Null(f.Row(f.OpenAdminId).ManualByEmployeeId);
        // A refusal is not an edit. An audit trail that records both stops being evidence of either.
        Assert.Equal(0, f.AuditRows());
    }

    [Fact]
    public async Task A_manager_cannot_write_a_time_onto_another_branchs_day()
    {
        // Update takes a record id straight from the URL, so the filter on the list protects nothing
        // here — the id of a record they never saw works just as well as one they did.
        using var f = new Fixture();

        AssertOutOfScope(await f.AsManager().Update(f.OpenTheirsId, CloseAt(Fixture.OpenDate)));

        Assert.Null(f.Row(f.OpenTheirsId).CheckOutAtUtc);
        Assert.Equal(0, f.AuditRows());
    }

    // --- undoing a check-out ----------------------------------------------------------------------

    [Fact]
    public async Task A_manager_undoes_their_own_staffs_accidental_check_out()
    {
        // A double scan at the gate checks somebody out minutes after they arrived; without this their
        // whole day is five minutes long, and Update cannot fix it (a null check-out means "leave").
        using var f = new Fixture();

        Assert.IsType<OkObjectResult>(await f.AsManager().ClearCheckOut(f.ClosedMineId, Why));

        Assert.Null(f.Row(f.ClosedMineId).CheckOutAtUtc);
        Assert.Equal(f.ManagerId, f.Row(f.ClosedMineId).ManualByEmployeeId);
    }

    [Fact]
    public async Task A_manager_cannot_reopen_a_same_branch_admins_day()
    {
        // Same target, the other verb. Both writes have to ask the same question, or the boundary is
        // only as strong as whichever endpoint the next change remembers.
        using var f = new Fixture();

        AssertOutOfScope(await f.AsManager().ClearCheckOut(f.ClosedAdminId, Why));

        Assert.Equal(At(Fixture.ClosedDate, 18), f.Row(f.ClosedAdminId).CheckOutAtUtc);
        Assert.Equal(0, f.AuditRows());
    }

    [Fact]
    public async Task A_manager_cannot_reopen_another_branchs_day()
    {
        using var f = new Fixture();

        AssertOutOfScope(await f.AsManager().ClearCheckOut(f.ClosedTheirsId, Why));

        Assert.Equal(At(Fixture.ClosedDate, 18), f.Row(f.ClosedTheirsId).CheckOutAtUtc);
    }

    [Fact]
    public async Task An_admin_passes_through_both_writes()
    {
        // The corrections an admin could always make. If scoping ever starts applying to them too, the
        // records nobody else can reach — another branch's, another admin's — become uncorrectable.
        using var f = new Fixture();

        Assert.IsType<OkObjectResult>(await f.AsAdmin().Update(f.OpenAdminId, CloseAt(Fixture.OpenDate)));
        Assert.IsType<OkObjectResult>(await f.AsAdmin().ClearCheckOut(f.ClosedTheirsId, Why));

        Assert.Equal(At(Fixture.OpenDate, 18), f.Row(f.OpenAdminId).CheckOutAtUtc);
        Assert.Null(f.Row(f.ClosedTheirsId).CheckOutAtUtc);
    }

    // --- what the widening did NOT hand over ------------------------------------------------------

    [Fact]
    public void RecheckFaces_stays_admin_only()
    {
        // The class says "Admin,Manager", so this is protected by nothing except the attribute on the
        // method itself, and it is not a scoping question: one call spends a paid Rekognition request
        // per record across the WHOLE tenant, which is not a branch-sized action however narrow the
        // caller's branch is. Drop the attribute and it quietly becomes a manager power with no other
        // test failing.
        var method = typeof(AdminAttendanceController).GetMethod("RecheckFaces", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(method);
        Assert.Equal("Admin", method!.GetCustomAttribute<AuthorizeAttribute>()?.Roles);
    }

    [Fact]
    public void Create_is_no_longer_admin_only_and_must_never_go_back_to_being_unguarded()
    {
        // It carried the attribute because "there is no existing record to scope against". True of the
        // record, and it was the wrong thing to scope against: the anchor is the EMPLOYEE the day is
        // being written onto, which exists whether or not a record does. So the attribute is gone and
        // an explicit scope check stands in its place — CanManageEmployeeAsync until 2026-10-15, now
        // CanCorrectAttendanceAsync, which also refuses one's own day. The three tests above are what hold
        // it, and this one fails loudly if somebody removes the check without restoring the attribute.
        var method = typeof(AdminAttendanceController).GetMethod("Create", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(method);
        Assert.Null(method!.GetCustomAttribute<AuthorizeAttribute>());

        var body = File.ReadAllText(Path.Combine(SourceDir(), "src", "AttendanceQR.Api", "Controllers",
            "AdminAttendanceController.cs"));
        var create = body[body.IndexOf("public async Task<IActionResult> Create(")..];
        create = create[..create.IndexOf("_db.AttendanceRecords.Add")];
        // Since 2026-10-16 through CorrectorAsync: today's rule for somebody else's day, the single-admin rule
        // (SelfCorrection) for one's own. Both must stay in it.
        Assert.Contains("CorrectorAsync(", create);
        var corrector = body[body.IndexOf("private async Task<(IActionResult? Refusal, SelfCorrectionAnswer? Self)> CorrectorAsync(")..];
        corrector = corrector[..corrector.IndexOf("return (null, null);")];
        Assert.Contains("CanCorrectAttendanceAsync", corrector);
        Assert.Contains("SelfCorrection.AskAsync", corrector);
    }

    /// <summary>Walks up from the test binary to the repository root.</summary>
    private static string SourceDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
            dir = Directory.GetParent(dir)?.FullName;
        Assert.NotNull(dir);
        return dir!;
    }

    [Fact]
    public void The_screen_a_manager_gained_says_so_on_the_class()
    {
        var attr = typeof(AdminAttendanceController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("Admin,Manager", attr!.Roles);
    }

    // --- writing a day from nothing ----------------------------------------------------------

    /// <summary>
    /// Creating an attendance record where none exists.
    ///
    /// It was Admin-only, on the reasoning that writing a day out of nothing is a different power from
    /// correcting one that already exists. Which is true, and still left the manager stuck in the case
    /// that actually happens: somebody leaves their phone at home and never scans, so there is nothing
    /// to correct — and the person who knows they were at work is the manager who saw them there.
    /// </summary>
    [Fact]
    public async Task A_manager_can_record_a_day_for_their_own_worker_who_never_scanned()
    {
        using var h = new Fixture();
        var day = new DateOnly(2026, 8, 20);

        var result = await h.AsManager().Create(new AdminAttendanceCreateRequest(
            h.MyWorkerId, day, At(day, 9), At(day, 18), "Telefon evdə qalıb"));

        Assert.IsType<OkObjectResult>(result);
        Assert.True(h.Db.AttendanceRecords.AsNoTracking()
            .Any(r => r.EmployeeId == h.MyWorkerId && r.AttendanceDate == day));
    }

    [Fact]
    public async Task A_manager_cannot_record_a_day_for_another_branch()
    {
        using var h = new Fixture();
        var day = new DateOnly(2026, 8, 20);

        var result = await h.AsManager().Create(new AdminAttendanceCreateRequest(
            h.TheirWorkerId, day, At(day, 9), At(day, 18), "Telefon evdə qalıb"));

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);
        // Date-specific: the fixture already seeds this worker an open record on another day.
        Assert.False(h.Db.AttendanceRecords.AsNoTracking()
            .Any(r => r.EmployeeId == h.TheirWorkerId && r.AttendanceDate == day));
    }

    [Fact]
    public async Task A_manager_cannot_record_a_day_for_an_admin_at_their_own_branch()
    {
        // The 2026-08-08 boundary, and it holds here for the same reason it holds for a PIN reset:
        // writing attendance onto the account that can reset PINs is not a smaller version of the
        // thing that was forbidden.
        using var h = new Fixture();
        var day = new DateOnly(2026, 8, 20);

        var result = await h.AsManager().Create(new AdminAttendanceCreateRequest(
            h.SameBranchAdminId, day, At(day, 9), At(day, 18), "Telefon evdə qalıb"));

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.False(h.Db.AttendanceRecords.AsNoTracking()
            .Any(r => r.EmployeeId == h.SameBranchAdminId && r.AttendanceDate == day));
    }

    [Fact]
    public async Task An_admin_can_still_record_a_day_for_anyone()
    {
        using var h = new Fixture();
        var day = new DateOnly(2026, 8, 20);

        Assert.IsType<OkObjectResult>(await h.AsAdmin().Create(new AdminAttendanceCreateRequest(
            h.TheirWorkerId, day, At(day, 9), At(day, 18), "Telefon evdə qalıb")));
    }

    // ── The owner's rules of 2026-10-15: a correction says why, the why is kept, and nobody corrects their own day.

    [Fact]
    public async Task Every_correction_says_why_or_is_refused_and_nothing_is_written()
    {
        using var f = new Fixture();
        var admin = f.AsAdmin();
        var day = Fixture.OpenDate.AddDays(-1);

        foreach (var reason in new[] { null, "", "   ", "ok" })
        {
            AssertReasonRequired(await admin.Update(f.OpenMineId, new AdminAttendanceUpdateRequest(null, At(Fixture.OpenDate, 18), reason)));
            AssertReasonRequired(await admin.CloseOpenDays(new CloseOpenDaysRequest([f.OpenMineId], reason)));
            AssertReasonRequired(await admin.Create(new AdminAttendanceCreateRequest(f.MyWorkerId, day, At(day, 9), At(day, 18), reason)));
            AssertReasonRequired(await admin.ClearCheckOut(f.ClosedMineId, new AttendanceCorrectionRequest(reason)));
            AssertReasonRequired(await admin.Delete(f.ClosedMineId, new AttendanceCorrectionRequest(reason)));
        }
        // No body at all — what an older screen sends — is the same refusal, not a framework error.
        AssertReasonRequired(await admin.ClearCheckOut(f.ClosedMineId, null));
        AssertReasonRequired(await admin.Delete(f.ClosedMineId, null));

        Assert.Null(f.Row(f.OpenMineId).CheckOutAtUtc);
        Assert.NotNull(f.Row(f.ClosedMineId).CheckOutAtUtc);
        Assert.False(f.Db.AttendanceRecords.Any(r => r.EmployeeId == f.MyWorkerId && r.AttendanceDate == day));
        Assert.Empty(f.Db.AuditLogs);
    }

    [Fact]
    public async Task The_reason_is_kept_in_the_audit_line_with_what_changed()
    {
        using var f = new Fixture();

        Assert.IsType<OkObjectResult>(await f.AsAdmin().Update(f.OpenMineId,
            new AdminAttendanceUpdateRequest(null, At(Fixture.OpenDate, 18), "  Çıxış vurmağı unudub  ")));

        var line = Assert.Single(f.Db.AuditLogs.Where(a => a.EventType == AuditEventType.RecordEditedByAdmin));
        Assert.Equal(f.MyWorkerId, line.EmployeeId);
        Assert.Contains($"By {f.AdminId}", line.Reason);
        Assert.Contains("09:00–— UTC → 09:00–18:00 UTC", line.Reason);
        Assert.EndsWith("Səbəb: Çıxış vurmağı unudub", line.Reason);
    }

    [Fact]
    public async Task Nobody_corrects_their_own_day_an_admin_included()
    {
        using var f = new Fixture();
        var ownOpen = Guid.NewGuid();
        var ownClosed = Guid.NewGuid();
        f.Db.AttendanceRecords.Add(new AttendanceRecord
        {
            Id = ownOpen, TenantId = TenantId, EmployeeId = f.AdminId, LocationId = f.ManagedBranch,
            AttendanceDate = Fixture.OpenDate, CheckInAtUtc = At(Fixture.OpenDate, 9), Status = AttendanceStatus.Incomplete,
        });
        f.Db.AttendanceRecords.Add(new AttendanceRecord
        {
            Id = ownClosed, TenantId = TenantId, EmployeeId = f.AdminId, LocationId = f.ManagedBranch,
            AttendanceDate = Fixture.ClosedDate, CheckInAtUtc = At(Fixture.ClosedDate, 9),
            CheckOutAtUtc = At(Fixture.ClosedDate, 18), Status = AttendanceStatus.OnTime,
        });
        f.Db.SaveChanges();
        var admin = f.AsAdmin();
        var why = "Özümə düzəliş";
        var day = Fixture.OpenDate.AddDays(-1);

        AssertRefused(await admin.Update(ownOpen, new AdminAttendanceUpdateRequest(null, At(Fixture.OpenDate, 18), why)), "OwnAttendance");
        AssertRefused(await admin.ClearCheckOut(ownClosed, new AttendanceCorrectionRequest(why)), "OwnAttendance");
        AssertRefused(await admin.Delete(ownClosed, new AttendanceCorrectionRequest(why)), "OwnAttendance");
        AssertRefused(await admin.Create(new AdminAttendanceCreateRequest(f.AdminId, day, At(day, 9), At(day, 18), why)), "OwnAttendance");

        // In a batch the own day is skipped and counted, never closed — the others still close.
        var batch = Assert.IsType<OkObjectResult>(await admin.CloseOpenDays(new CloseOpenDaysRequest([ownOpen, f.OpenMineId], why)));
        Assert.Equal((1, 1), ((int)batch.Value!.GetType().GetProperty("closed")!.GetValue(batch.Value)!,
                              (int)batch.Value!.GetType().GetProperty("skipped")!.GetValue(batch.Value)!));
        Assert.Null(f.Row(ownOpen).CheckOutAtUtc);
        Assert.NotNull(f.Row(ownClosed).CheckOutAtUtc);
        // And the list says so before anybody presses anything.
        Assert.False(Closable(await f.AsAdmin().Open())[ownOpen]);
    }

    private static void AssertReasonRequired(IActionResult result)
    {
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("ReasonRequired", bad.Value!.GetType().GetProperty("error")!.GetValue(bad.Value));
    }

    // ── The other six ways a day changes (2026-10-15, the owner's second decision): Qayıb on and off, a
    //    missed check-out approved or rejected, a voided day — each says why, and the why is kept.

    private static T As<T>(T controller, Guid who, EmployeeRole role) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim("sub", who.ToString()), new Claim("role", role.ToString()),
                }, "test")),
            },
        };
        return controller;
    }

    private sealed class NoPush : IPushNotifier
    {
        public Task<int> NotifyEmployeesAsync(IReadOnlyCollection<Guid> employeeIds, string title, string body, string? url,
            CancellationToken ct = default) => Task.FromResult(0);
    }

    private static string LastAudit(Fixture f) =>
        f.Db.AuditLogs.AsEnumerable().Last(a => a.EventType == AuditEventType.RecordEditedByAdmin).Reason!;

    [Fact]
    public async Task A_Qayib_is_put_on_and_taken_off_only_with_a_reason_and_both_are_on_the_record()
    {
        using var f = new Fixture();
        var marks = As(new AbsenceMarksController(f.Db, new StubSummary()), f.AdminId, EmployeeRole.Admin);
        var day = Fixture.OpenDate.AddDays(-10);

        AssertReasonRequired(await marks.Mark(new AbsenceMarkRequest(f.MyWorkerId, day, null)));
        AssertReasonRequired(await marks.Mark(new AbsenceMarkRequest(f.MyWorkerId, day, "x")));
        Assert.Empty(f.Db.AbsenceMarks);

        Assert.IsType<OkObjectResult>(await marks.Mark(new AbsenceMarkRequest(f.MyWorkerId, day, "Xəbərsiz gəlmədi")));
        Assert.Contains($"Qayıb qoyuldu {day:yyyy-MM-dd}. Səbəb: Xəbərsiz gəlmədi", LastAudit(f));

        AssertReasonRequired(await marks.Unmark(f.MyWorkerId, day, null));
        Assert.Single(f.Db.AbsenceMarks);
        Assert.IsType<OkObjectResult>(await marks.Unmark(f.MyWorkerId, day, new AttendanceCorrectionRequest("Arayış gətirdi")));
        Assert.Empty(f.Db.AbsenceMarks);
        Assert.Contains("Qayıb götürüldü", LastAudit(f));
        Assert.EndsWith("Səbəb: Arayış gətirdi", LastAudit(f));
    }

    [Fact]
    public async Task A_missed_check_out_is_approved_or_rejected_only_with_a_reason()
    {
        using var f = new Fixture();
        MissedCheckoutRequest Request() => new()
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = f.MyWorkerId, AttendanceRecordId = f.OpenMineId,
            AttendanceDate = Fixture.OpenDate, RequestedCheckOutAtUtc = At(Fixture.OpenDate, 17), Reason = "Telefon sönmüşdü",
            RequestedAtUtc = DateTime.UtcNow,
        };
        var approve = Request();
        var reject = Request();
        f.Db.MissedCheckoutRequests.AddRange(approve, reject);
        f.Db.SaveChanges();
        var desk = As(new AdminMissedCheckoutController(f.Db, new StubSummary()), f.AdminId, EmployeeRole.Admin);

        AssertReasonRequired(await desk.Approve(approve.Id, null));
        AssertReasonRequired(await desk.Reject(reject.Id, new AttendanceCorrectionRequest("  ")));
        Assert.All(f.Db.MissedCheckoutRequests, r => Assert.Equal(MissedCheckoutStatus.Pending, r.Status));

        Assert.IsType<OkObjectResult>(await desk.Approve(approve.Id, new AttendanceCorrectionRequest("Rəhbəri təsdiqlədi")));
        Assert.Contains("işçinin səbəbi: Telefon sönmüşdü", LastAudit(f));
        Assert.EndsWith("Səbəb: Rəhbəri təsdiqlədi", LastAudit(f));
        Assert.IsType<OkObjectResult>(await desk.Reject(reject.Id, new AttendanceCorrectionRequest("Artıq bağlanıb")));
        Assert.EndsWith("Səbəb: Artıq bağlanıb", LastAudit(f));
    }

    [Fact]
    public async Task A_day_is_voided_or_restored_only_with_a_reason()
    {
        using var f = new Fixture();
        var admin = f.AsAdmin();

        // Refused before anything else is read — the notifier is never reached.
        AssertReasonRequired(await admin.VoidFraud(f.ClosedMineId, new VoidFraudRequest(Reason: null), pushNotifier: null!));
        Assert.Null(f.Row(f.ClosedMineId).VoidedAtUtc);

        // With a reason it goes through, and its own line says why the way every correction does.
        Assert.IsType<OkObjectResult>(await admin.VoidFraud(f.ClosedMineId,
            new VoidFraudRequest("Giriş şəkli saxtadır", RevokeDevice: false, NotifyEmployee: false), new NoPush()));
        Assert.NotNull(f.Row(f.ClosedMineId).VoidedAtUtc);
        Assert.EndsWith("Səbəb: Giriş şəkli saxtadır", LastAudit(f));
        AssertReasonRequired(await admin.Unvoid(f.ClosedMineId, null));
        Assert.NotNull(f.Row(f.ClosedMineId).VoidedAtUtc);
        Assert.IsType<OkObjectResult>(await admin.Unvoid(f.ClosedMineId, new AttendanceCorrectionRequest("Şəkil yenidən yoxlandı — özüdür")));
        Assert.Null(f.Row(f.ClosedMineId).VoidedAtUtc);
        Assert.Contains("ləğvin səbəbi: Giriş şəkli saxtadır", LastAudit(f));
        Assert.EndsWith("Səbəb: Şəkil yenidən yoxlandı — özüdür", LastAudit(f));
    }
}
