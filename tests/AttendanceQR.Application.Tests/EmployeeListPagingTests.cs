using System.Security.Claims;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Application.Common;
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
/// The roster list, after it stopped being «the whole company, filtered in the browser».
///
/// It used to load every employee WITH every device binding each of them had ever had, plus all
/// locations, all schedules, all managed-location rows and every push subscription, and then search,
/// count and page in JavaScript. On production that path had read 227 million rows out of a 1,081-row
/// table. Now one page of twenty-five people costs one page of twenty-five rows.
///
/// These tests are about the three things that could quietly have broken in that move:
///
/// 1. The numbers. The cards and the «Diqqət tələb edir» tiles counted the WHOLE company; a page of
///    twenty-five cannot, so they come from /stats and must still mean the same five words.
/// 2. The selection. «Hamısını seç» and ⌘A have to keep meaning «everyone this filter matches».
///    If paging had quietly shrunk them to «these twenty-five», a permission granted to a branch of
///    forty would have reached the first page of it and nobody would have noticed.
/// 3. The edit form. The list DTO no longer carries email, salary or the per-person overrides, and
///    <c>EmployeeUpdateRequest</c> null-defaults every field it is not handed — so a form fed from a
///    list row would BLANK them. The form reads /employees/{id}, which must carry all of it.
/// </summary>
public class EmployeeListPagingTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-00000000a003");
    private static readonly Guid OtherTenantId = Guid.Parse("00000000-0000-0000-0000-00000000b003");

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public AdminController Admin { get; }
        public Guid BranchA { get; } = Guid.NewGuid();
        public Guid BranchB { get; } = Guid.NewGuid();

        public Harness()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase($"emp-list-{Guid.NewGuid()}").Options,
                tenant);

            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "A", Slug = "a", DisplayName = "A", IsActive = true });
            Db.Tenants.Add(new Tenant { Id = OtherTenantId, Name = "B", Slug = "b", DisplayName = "B", IsActive = true });
            foreach (var (id, name) in new[] { (BranchA, "Mərkəz"), (BranchB, "Novxanı") })
                Db.Locations.Add(new Location
                {
                    Id = id, TenantId = TenantId, Name = name,
                    Latitude = 40.4, Longitude = 49.8, RadiusMeters = 150,
                    ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
                    LateThresholdMinutes = 15, QrVersion = 1, IsActive = true,
                });
            Db.SaveChanges();

            Admin = new AdminController(
                Db,
                Options.Create(new InvitationOptions()),
                new PasswordHasher(),
                new MemoryCacheLoginLockoutStore(new MemoryCache(new MemoryCacheOptions())),
                // The system/root accounts a tenant admin must not be shown as staff.
                new AppOptions { TimeZone = "Asia/Baku", HiddenEmails = "root@qrlog.az" },
                new NoPhotos(),
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
        }

        public Guid Person(
            string name,
            Guid? branch = null,
            bool active = true,
            bool activated = true,
            bool mustChangePin = false,
            EmployeeRole role = EmployeeRole.Employee,
            string? phone = null,
            string? position = null,
            string? email = null,
            Guid? tenant = null)
        {
            var id = Guid.NewGuid();
            Db.Employees.Add(new Employee
            {
                Id = id, TenantId = tenant ?? TenantId, FullName = name, Role = role,
                IsActive = active, PasswordHash = "h", LocationId = branch ?? BranchA,
                ActivatedAtUtc = activated ? DateTime.UtcNow : null,
                MustChangePin = mustChangePin, PhoneNumber = phone, Position = position, Email = email,
            });
            Db.SaveChanges();
            return id;
        }

        public void Device(Guid employeeId, string label)
        {
            Db.DeviceBindings.Add(new DeviceBinding
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = employeeId,
                DeviceFingerprint = Guid.NewGuid().ToString("N"), DeviceLabel = label,
                IsActive = true, BoundAtUtc = DateTime.UtcNow, LastSeenAtUtc = DateTime.UtcNow,
            });
            Db.SaveChanges();
        }

        public void Push(Guid employeeId)
        {
            Db.PushSubscriptions.Add(new PushSubscription
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = employeeId,
                Endpoint = "https://push.example/" + Guid.NewGuid().ToString("N"),
                P256dh = "k", Auth = "a", CreatedAtUtc = DateTime.UtcNow,
            });
            Db.SaveChanges();
        }

        public void Dispose() => Db.Dispose();
    }

    /// <summary>The controller needs one; nothing here touches a photo.</summary>
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

    // --- reading the anonymous DTOs back ------------------------------------

    private static object Ok(IActionResult r) => Assert.IsType<OkObjectResult>(r).Value!;

    private static T Get<T>(object dto, string name) =>
        (T)dto.GetType().GetProperty(name)!.GetValue(dto)!;

    private static T? GetOrNull<T>(object dto, string name) where T : class =>
        dto.GetType().GetProperty(name)!.GetValue(dto) as T;

    private static List<object> Items(IActionResult r) =>
        ((System.Collections.IEnumerable)Get<object>(Ok(r), "items")).Cast<object>().ToList();

    private static List<Guid> Ids(IActionResult r) =>
        Items(r).Select(i => Get<Guid>(i, "id")).ToList();

    private static List<Guid> SelectionIds(IActionResult r) =>
        ((System.Collections.IEnumerable)Ok(r)).Cast<object>().Select(i => Get<Guid>(i, "id")).ToList();

    // --- paging -------------------------------------------------------------

    [Fact]
    public async Task A_page_is_a_page_and_the_total_is_the_whole_match()
    {
        using var h = new Harness();
        for (var i = 0; i < 60; i++) h.Person($"İşçi {i:D2}");

        var first = await h.Admin.List(page: 1, pageSize: 25);

        Assert.Equal(60, Get<int>(Ok(first), "total"));
        Assert.Equal(25, Ids(first).Count);
    }

    [Fact]
    public async Task Page_two_is_different_people_and_the_last_page_is_the_remainder()
    {
        using var h = new Harness();
        for (var i = 0; i < 60; i++) h.Person($"İşçi {i:D2}");

        var p1 = Ids(await h.Admin.List(page: 1, pageSize: 25));
        var p2 = Ids(await h.Admin.List(page: 2, pageSize: 25));
        var p3 = Ids(await h.Admin.List(page: 3, pageSize: 25));

        Assert.Empty(p1.Intersect(p2));
        Assert.Empty(p1.Intersect(p3));
        Assert.Equal(10, p3.Count);
        // Nobody is skipped and nobody is served twice — the whole roster, exactly once.
        Assert.Equal(60, p1.Concat(p2).Concat(p3).Distinct().Count());
    }

    [Fact]
    public async Task The_order_breaks_ties_so_paging_cannot_drift()
    {
        // Thirty people with the SAME name. Without a tiebreaker the database is free to order them
        // differently per query, and somebody shows up on both page 1 and page 2 while somebody else
        // shows up on neither.
        using var h = new Harness();
        for (var i = 0; i < 30; i++) h.Person("Eyni Ad");

        var a = Ids(await h.Admin.List(page: 1, pageSize: 10)).Concat(Ids(await h.Admin.List(page: 2, pageSize: 10))).ToList();
        var b = Ids(await h.Admin.List(page: 1, pageSize: 10)).Concat(Ids(await h.Admin.List(page: 2, pageSize: 10))).ToList();

        Assert.Equal(a, b);
        Assert.Equal(20, a.Distinct().Count());
    }

    [Fact]
    public async Task A_nonsense_page_size_falls_back_rather_than_loading_the_company()
    {
        using var h = new Harness();
        for (var i = 0; i < 40; i++) h.Person($"İşçi {i:D2}");

        // The browser is not the thing that decides how much of the table to read.
        Assert.Equal(25, Ids(await h.Admin.List(page: 1, pageSize: 100_000)).Count);
        Assert.Equal(25, Ids(await h.Admin.List(page: 1, pageSize: 0)).Count);
        Assert.Equal(25, Ids(await h.Admin.List(page: -5, pageSize: 25)).Count);
    }

    // --- filtering, in SQL rather than in the browser -----------------------

    [Fact]
    public async Task Search_matches_the_same_four_fields_the_browser_used_to()
    {
        using var h = new Harness();
        var byName = h.Person("Ramin Həsənov");
        var byPhone = h.Person("Nicat Quliyev", phone: "556667788");
        var byPosition = h.Person("Elçin Vəliyev", position: "Elektrik");
        h.Person("Kənar Adam");

        Assert.Equal([byName], Ids(await h.Admin.List(search: "həsənov")));
        Assert.Equal([byPhone], Ids(await h.Admin.List(search: "5566677")));
        Assert.Equal([byPosition], Ids(await h.Admin.List(search: "elektrik")));
        // The list prints an eight-character id prefix, and people paste it back in.
        Assert.Equal([byName], Ids(await h.Admin.List(search: byName.ToString()[..8])));
        Assert.Empty(Ids(await h.Admin.List(search: "belə adam yoxdur")));
    }

    [Fact]
    public async Task The_branch_filter_narrows_the_page_and_the_total()
    {
        using var h = new Harness();
        for (var i = 0; i < 30; i++) h.Person($"A {i:D2}", h.BranchA);
        for (var i = 0; i < 5; i++) h.Person($"B {i:D2}", h.BranchB);

        var b = await h.Admin.List(page: 1, pageSize: 25, locationId: h.BranchB);

        Assert.Equal(5, Get<int>(Ok(b), "total"));
        Assert.Equal(5, Ids(b).Count);
    }

    [Fact]
    public async Task The_role_filter_narrows_the_page()
    {
        using var h = new Harness();
        h.Person("Adi İşçi");
        var manager = h.Person("Menecer", role: EmployeeRole.Manager);

        Assert.Equal([manager], Ids(await h.Admin.List(role: "Manager")));
        // An unparseable role is ignored rather than returning nothing — a stale bookmark must not
        // read as «this company has no staff».
        Assert.Equal(2, Ids(await h.Admin.List(role: "Dragon")).Count);
    }

    [Fact]
    public async Task Activated_and_pending_mean_what_the_picker_says()
    {
        using var h = new Harness();
        // Signed in and chose their own PIN.
        var started = h.Person("Başlayan", activated: true, mustChangePin: false);
        // The invite was never accepted.
        var invited = h.Person("Dəvətli", activated: false);
        // Activated at creation, still on the PIN an admin generated — started by neither measure.
        h.Person("Müvəqqəti PIN", activated: true, mustChangePin: true);

        Assert.Equal([started], Ids(await h.Admin.List(status: "activated")));
        Assert.Equal([invited], Ids(await h.Admin.List(status: "pending")));
    }

    [Fact]
    public async Task Not_started_covers_both_ways_of_never_getting_going()
    {
        using var h = new Harness();
        h.Person("Başlayan");
        var invited = h.Person("Dəvətli", activated: false);
        var tempPin = h.Person("Müvəqqəti PIN", mustChangePin: true);

        var got = Ids(await h.Admin.List(status: "notstarted"));

        Assert.Equal(2, got.Count);
        Assert.Contains(invited, got);
        Assert.Contains(tempPin, got);
    }

    [Fact]
    public async Task No_push_and_no_device_find_the_people_nobody_can_reach()
    {
        using var h = new Harness();
        var reachable = h.Person("Çatılan");
        var unreachable = h.Person("Çatılmayan");
        h.Push(reachable);
        h.Device(reachable, "Redmi");

        Assert.Equal([unreachable], Ids(await h.Admin.List(status: "nopush")));
        Assert.Equal([unreachable], Ids(await h.Admin.List(status: "nodevice")));
    }

    [Fact]
    public async Task A_leaver_is_on_the_leavers_list_and_only_there()
    {
        using var h = new Harness();
        var working = h.Person("İşləyən");
        var left = h.Person("Çıxan", active: false);

        Assert.Equal([working], Ids(await h.Admin.List()));
        Assert.Equal([left], Ids(await h.Admin.List(showLeft: true)));
    }

    [Fact]
    public async Task The_system_accounts_are_not_staff()
    {
        using var h = new Harness();
        var staff = h.Person("Adi İşçi");
        h.Person("Root", email: "root@qrlog.az", role: EmployeeRole.Admin);

        // Operators live inside a tenant as ordinary-looking rows; they are not on its roster.
        Assert.Equal([staff], Ids(await h.Admin.List()));
        Assert.Equal(1, Get<int>(Ok(await h.Admin.Stats()), "total"));
    }

    // --- multi-tenancy ------------------------------------------------------

    [Fact]
    public async Task Another_company_is_not_on_the_page_in_the_counts_or_in_the_selection()
    {
        using var h = new Harness();
        var mine = h.Person("Bizim");
        var theirs = h.Person("Özgə", tenant: OtherTenantId);

        Assert.Equal([mine], Ids(await h.Admin.List()));
        Assert.Equal(1, Get<int>(Ok(await h.Admin.Stats()), "total"));
        Assert.Equal([mine], SelectionIds(await h.Admin.Selection()));
        // And a direct read of their id is «not found», not a refusal — the row simply is not there.
        Assert.IsType<NotFoundObjectResult>(await h.Admin.Detail(theirs));
    }

    // --- the device facts, without loading the bindings ---------------------

    [Fact]
    public async Task The_newest_device_and_the_count_come_back_without_materialising_the_bindings()
    {
        using var h = new Harness();
        var busy = h.Person("Üç cihazlı");
        h.Device(busy, "Redmi 9A");
        h.Device(busy, "Samsung A12");
        h.Device(busy, "iPhone SE");
        h.Person("Cihazsız");
        // The seeding tracked its own inserts; from here on, anything in the tracker came out of the
        // query under test.
        h.Db.ChangeTracker.Clear();

        var result = await h.Admin.List();
        var row = Items(result).Single(i => Get<Guid>(i, "id") == busy);

        Assert.True(Get<bool>(row, "hasDevice"));
        Assert.Equal(3, Get<int>(row, "deviceCount"));
        Assert.NotNull(GetOrNull<string>(row, "deviceLabel"));

        // The point of the change: the list reads scalars, it does not pull the collection. The old
        // `.Include(e => e.DeviceBindings)` brought back every binding an employee had ever had, to
        // show one label and a number.
        Assert.Empty(h.Db.ChangeTracker.Entries<DeviceBinding>());
        Assert.Empty(h.Db.ChangeTracker.Entries<Employee>());
    }

    // --- the counts above the list -----------------------------------------

    [Fact]
    public async Task The_counts_are_over_the_whole_company_not_over_the_page()
    {
        using var h = new Harness();
        for (var i = 0; i < 40; i++) h.Person($"Başlayan {i:D2}");
        for (var i = 0; i < 12; i++) h.Person($"Dəvətli {i:D2}", activated: false);

        var s = Ok(await h.Admin.Stats());

        Assert.Equal(52, Get<int>(s, "total"));
        Assert.Equal(40, Get<int>(s, "activated"));
        Assert.Equal(12, Get<int>(s, "notStarted"));
    }

    [Fact]
    public async Task The_counts_follow_the_branch_the_reader_is_looking_at()
    {
        using var h = new Harness();
        for (var i = 0; i < 9; i++) h.Person($"A {i}", h.BranchA);
        for (var i = 0; i < 4; i++) h.Person($"B {i}", h.BranchB, activated: false);

        Assert.Equal(13, Get<int>(Ok(await h.Admin.Stats()), "total"));
        Assert.Equal(4, Get<int>(Ok(await h.Admin.Stats(h.BranchB)), "total"));
        Assert.Equal(4, Get<int>(Ok(await h.Admin.Stats(h.BranchB)), "notStarted"));
    }

    [Fact]
    public async Task Bildiris_catmir_counts_only_people_who_have_actually_started()
    {
        // Somebody who has never opened the app has no push subscription for a reason that is not
        // about notifications; counting them would hide how well the reachable ones are covered.
        using var h = new Harness();
        var startedNoPush = h.Person("Başlayıb, bildiriş yox");
        var startedWithPush = h.Person("Başlayıb, bildiriş var");
        h.Push(startedWithPush);
        h.Person("Heç vaxt açmayıb", activated: false);

        Assert.Equal(1, Get<int>(Ok(await h.Admin.Stats()), "noPush"));
        Assert.Equal(3, Get<int>(Ok(await h.Admin.Stats()), "noDevice"));
        Assert.NotEqual(Guid.Empty, startedNoPush);
    }

    [Fact]
    public async Task The_leavers_count_is_company_wide_whichever_list_is_on_screen()
    {
        using var h = new Harness();
        h.Person("İşləyən", h.BranchA);
        h.Person("Çıxan A", h.BranchA, active: false);
        h.Person("Çıxan B", h.BranchB, active: false);

        // The button that opens the leavers list carries the number, so a branch filter must not hide
        // the people the admin is about to go looking for.
        Assert.Equal(2, Get<int>(Ok(await h.Admin.Stats()), "leftCount"));
        Assert.Equal(2, Get<int>(Ok(await h.Admin.Stats(h.BranchA)), "leftCount"));
    }

    // --- «Hamısını seç» -----------------------------------------------------

    [Fact]
    public async Task Select_all_returns_everyone_the_filter_matches_not_one_page()
    {
        // The invariant this whole endpoint exists for. A branch is routinely forty people, and
        // «grant this to the branch» must not silently become «grant it to the first twenty-five».
        using var h = new Harness();
        for (var i = 0; i < 60; i++) h.Person($"İşçi {i:D2}");

        Assert.Equal(60, SelectionIds(await h.Admin.Selection()).Count);
    }

    [Fact]
    public async Task Select_all_honours_the_same_filters_as_the_list()
    {
        using var h = new Harness();
        for (var i = 0; i < 30; i++) h.Person($"A {i:D2}", h.BranchA);
        for (var i = 0; i < 7; i++) h.Person($"B {i:D2}", h.BranchB, activated: false);

        Assert.Equal(7, SelectionIds(await h.Admin.Selection(locationId: h.BranchB)).Count);
        Assert.Equal(7, SelectionIds(await h.Admin.Selection(status: "notstarted")).Count);
        Assert.Equal(30, SelectionIds(await h.Admin.Selection(status: "activated")).Count);
    }

    [Fact]
    public async Task Select_all_carries_the_flags_a_bulk_action_decides_from()
    {
        using var h = new Harness();
        var id = h.Person("Müvəqqəti PIN", mustChangePin: true);

        var row = ((System.Collections.IEnumerable)Ok(await h.Admin.Selection())).Cast<object>().Single();

        // «Müvəqqəti PIN ver» offers itself for exactly these people, and the branch beside the name
        // is what tells two people called Ramin apart in the announcements picker.
        Assert.Equal(id, Get<Guid>(row, "id"));
        Assert.True(Get<bool>(row, "mustChangePin"));
        Assert.True(Get<bool>(row, "activated"));
        Assert.True(Get<bool>(row, "isActive"));
        Assert.False(Get<bool>(row, "canShareDevice"));
        Assert.False(Get<bool>(row, "canFieldCheckIn"));
        Assert.Equal("Mərkəz", GetOrNull<string>(row, "locationName"));
    }

    // --- the edit form ------------------------------------------------------

    [Fact]
    public async Task Detail_carries_every_field_the_edit_form_round_trips()
    {
        // This is the EmployeeUpdateRequest trap, as a test. The list DTO deliberately does not carry
        // these; if the form were fed from a list row, saving it would blank them.
        using var h = new Harness();
        var id = h.Person("Ramin Həsənov", phone: "556667788", position: "Elektrik", email: "r@example.az");
        var e = h.Db.Employees.Single(x => x.Id == id);
        e.FirstName = "Ramin";
        e.LastName = "Həsənov";
        e.FatherName = "Əli oğlu";
        e.MonthlySalary = 740m;
        e.PhotoExempt = true;
        e.QrlessCheckInOverride = true;
        e.RequireGeofenceOverride = false;
        e.WorkCycleDays = 2;
        e.WorkCycleOnDays = 1;
        e.WorkCycleAnchor = new DateOnly(2026, 9, 1);
        e.BirthDate = new DateOnly(1988, 3, 14);
        e.PaperEmployer = "Green Garden MMC";
        e.PaperSite = "Novxanı";
        h.Db.SaveChanges();

        var dto = Ok(await h.Admin.Detail(id));

        Assert.Equal("r@example.az", GetOrNull<string>(dto, "email"));
        Assert.Equal("Ramin", GetOrNull<string>(dto, "firstName"));
        Assert.Equal("Həsənov", GetOrNull<string>(dto, "lastName"));
        Assert.Equal("Əli oğlu", GetOrNull<string>(dto, "fatherName"));
        Assert.Equal(740m, dto.GetType().GetProperty("monthlySalary")!.GetValue(dto));
        Assert.True(Get<bool>(dto, "photoExempt"));
        Assert.Equal(true, dto.GetType().GetProperty("qrlessCheckInOverride")!.GetValue(dto));
        Assert.Equal(false, dto.GetType().GetProperty("requireGeofenceOverride")!.GetValue(dto));
        Assert.Equal(2, dto.GetType().GetProperty("workCycleDays")!.GetValue(dto));
        Assert.Equal(new DateOnly(2026, 9, 1), dto.GetType().GetProperty("workCycleAnchor")!.GetValue(dto));
        Assert.Equal(new DateOnly(1988, 3, 14), dto.GetType().GetProperty("birthDate")!.GetValue(dto));
        Assert.Equal("Green Garden MMC", GetOrNull<string>(dto, "paperEmployer"));
        Assert.Equal("Novxanı", GetOrNull<string>(dto, "paperSite"));
        // And the facts the profile screen shows beside them.
        Assert.Equal("Mərkəz", GetOrNull<string>(dto, "locationName"));
        Assert.Equal("Elektrik", GetOrNull<string>(dto, "position"));
    }

    [Fact]
    public async Task Detail_reads_a_manager_s_branches_so_the_form_cannot_clear_them()
    {
        using var h = new Harness();
        var id = h.Person("Menecer", role: EmployeeRole.Manager);
        h.Db.ManagedLocations.Add(new ManagedLocation { EmployeeId = id, LocationId = h.BranchB, TenantId = TenantId });
        h.Db.SaveChanges();

        var dto = Ok(await h.Admin.Detail(id));
        var ids = (List<Guid>)dto.GetType().GetProperty("managedLocationIds")!.GetValue(dto)!;
        var names = (List<string>)dto.GetType().GetProperty("managedLocationNames")!.GetValue(dto)!;

        // A manager with no branches sees nothing at all; an edit that dropped them would be silent.
        Assert.Equal([h.BranchB], ids);
        Assert.Equal(["Novxanı"], names);
    }

    [Fact]
    public async Task Detail_on_an_unknown_id_is_a_plain_not_found()
    {
        using var h = new Harness();

        var r = Assert.IsType<NotFoundObjectResult>(await h.Admin.Detail(Guid.NewGuid()));

        Assert.Equal("EmployeeNotFound", GetOrNull<string>(r.Value!, "error"));
    }
}
