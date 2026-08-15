using System.Security.Claims;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Application.Common;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// «İş sübutu» — the checklist and the work photo on a field visit. The load-bearing property is the
/// house rule: NOTHING here may cost a worker their record. A failed upload, a failed tick, a denied
/// camera and an unavailable GPS must all still leave a completed check-out behind. The rest pins the
/// evidence semantics: only the visit's own worker ticks, ticking is an absolute set (so an offline
/// replay is a no-op), and the work photo can never be confused with a selfie.
/// </summary>
public class FieldProofOfWorkTests
{
    private static readonly Guid TenantA = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid TenantB = Guid.Parse("00000000-0000-0000-0000-0000000000b2");

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public FieldVisitController AsManager { get; }
        public FieldVisitController AsWorker { get; }
        public FieldVisitController AsOtherWorker { get; }
        public StubPhoto Photo { get; } = new();
        public Guid Branch { get; } = Guid.NewGuid();
        public Guid ManagerId { get; } = Guid.NewGuid();
        public Guid WorkerId { get; } = Guid.NewGuid();
        public Guid OtherWorkerId { get; } = Guid.NewGuid();

        public Harness()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantA);
            Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"fv-pow-{Guid.NewGuid()}").Options, tenant);

            Db.Tenants.Add(new Tenant { Id = TenantA, Name = "A", Slug = "a", DisplayName = "A", IsActive = true });
            Db.Tenants.Add(new Tenant { Id = TenantB, Name = "B", Slug = "b", DisplayName = "B", IsActive = true });
            Db.Locations.Add(new Location
            {
                Id = Branch, TenantId = TenantA, Name = "Filial", Latitude = 40.4, Longitude = 49.8,
                RadiusMeters = 150, ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
                LateThresholdMinutes = 15, QrVersion = 1, IsActive = true,
            });
            Db.ManagedLocations.Add(new ManagedLocation { EmployeeId = ManagerId, LocationId = Branch, TenantId = TenantA });
            Db.Employees.Add(Person(ManagerId, "Menecer", EmployeeRole.Manager));
            Db.Employees.Add(Person(WorkerId, "İşçi", EmployeeRole.Employee));
            Db.Employees.Add(Person(OtherWorkerId, "Başqa İşçi", EmployeeRole.Employee));
            Db.SaveChanges();

            AsManager = Controller(ManagerId, EmployeeRole.Manager);
            AsWorker = Controller(WorkerId, EmployeeRole.Employee);
            AsOtherWorker = Controller(OtherWorkerId, EmployeeRole.Employee);
        }

        private Employee Person(Guid id, string name, EmployeeRole role) => new()
        {
            Id = id, TenantId = TenantA, FullName = name, Role = role, LocationId = Branch,
            CanFieldCheckIn = true, IsActive = true, ActivatedAtUtc = DateTime.UtcNow, PasswordHash = "x",
        };

        private FieldVisitController Controller(Guid callerId, EmployeeRole role)
        {
            var identity = new ClaimsIdentity(new[]
            {
                new Claim("sub", callerId.ToString()),
                new Claim("role", role.ToString()),
            }, "test");
            return new FieldVisitController(Db, Photo, new StubPush(), new AppOptions { TimeZone = "Asia/Baku" })
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
                },
            };
        }

        /// <summary>Assigns a visit to the worker and returns its id.</summary>
        public async Task<Guid> AssignAsync(params string[] checklist)
        {
            var res = await AsManager.Assign(new AssignFieldVisitRequest(WorkerId, Checklist: checklist));
            var ok = Assert.IsType<OkObjectResult>(res);
            return (Guid)ok.Value!.GetType().GetProperty("id")!.GetValue(ok.Value)!;
        }

        public List<FieldVisitChecklistItem> Items(Guid visitId) => Db.FieldVisitChecklistItems
            .IgnoreQueryFilters().AsNoTracking()
            .Where(i => i.FieldVisitId == visitId).OrderBy(i => i.SortOrder).ToList();

        public FieldVisit Visit(Guid id) => Db.FieldVisits.IgnoreQueryFilters().AsNoTracking().Single(v => v.Id == id);

        public async Task CheckInAsync(Guid visitId) =>
            await AsWorker.CheckIn(visitId, new FieldCheckInRequest(40.4, 49.8));

        public void Dispose() => Db.Dispose();
    }

    private sealed class StubPhoto : IPhotoStorageService
    {
        public int WorkUploads { get; private set; }
        public bool ThrowOnWorkUpload { get; set; }
        public Task<string> UploadCheckInPhotoAsync(Guid e, Guid r, byte[] b, CancellationToken ct = default) => Task.FromResult("checkins/k.jpg");
        public Task<string> UploadReferencePhotoAsync(Guid e, byte[] b, CancellationToken ct = default) => Task.FromResult("reference/k.jpg");
        public Task<string> UploadFieldWorkPhotoAsync(Guid t, Guid v, byte[] b, CancellationToken ct = default)
        {
            if (ThrowOnWorkUpload) throw new InvalidOperationException("R2 down");
            WorkUploads++;
            return Task.FromResult($"fieldwork/{t}/{v}.jpg");
        }
        public Task<string> UploadTaskPhotoAsync(Guid t, Guid id, byte[] b, CancellationToken ct = default) => Task.FromResult("tasks/k.jpg");
        public Task<string> GetPresignedUrlAsync(string key, CancellationToken ct = default) => Task.FromResult($"https://r2/{key}");
        public Task<byte[]> GetBytesAsync(string key, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
        public Task DeleteByPrefixOlderThanAsync(string p, DateTime o, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> DeleteObjectsAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default) => Task.FromResult(keys.Count);
    }

    private sealed class StubPush : IPushNotifier
    {
        public Task<int> NotifyEmployeesAsync(IReadOnlyCollection<Guid> ids, string t, string b, string? u, CancellationToken ct = default)
            => Task.FromResult(0);
    }

    /// <summary>A 1×1 JPEG as a data URL — enough for the decode path.</summary>
    private const string TinyJpeg =
        "data:image/jpeg;base64,/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0a" +
        "HBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/wAALCAABAAEBAREA/8QAFAABAAAAAAAAAAAAAAAAAAAACf/EABQQAQAA" +
        "AAAAAAAAAAAAAAAAAAD/2gAIAQEAAD8AKp//2Q==";

    // --- assign: the manager's list is cleaned, never rejected ---------------------

    [Fact]
    public async Task Assign_cleans_the_checklist_and_never_rejects_it()
    {
        using var h = new Harness();
        var messy = new[] { "  Süpür  ", "", "   ", "süpür", "Zibili boşalt", new string('x', 200) }
            .Concat(Enumerable.Range(1, 30).Select(i => $"iş {i}")).ToArray();

        var id = await h.AssignAsync(messy);   // 200 despite 36 messy lines
        var items = h.Items(id);

        Assert.Equal(10, items.Count);                                  // capped
        Assert.Equal("Süpür", items[0].Label);                          // trimmed
        Assert.Equal("Zibili boşalt", items[1].Label);                  // "süpür" de-duped case-insensitively
        Assert.Equal(120, items[2].Label.Length);                       // truncated
        Assert.Equal(new[] { 0, 1, 2 }, items.Take(3).Select(i => i.SortOrder));
        Assert.All(items, i => Assert.False(i.IsDone));
    }

    [Fact]
    public async Task Assign_without_a_checklist_creates_a_visit_with_no_items()
    {
        using var h = new Harness();
        var id = await h.AssignAsync();
        Assert.Empty(h.Items(id));
    }

    // --- ticking: only the visit's own worker, absolute set ------------------------

    [Fact]
    public async Task Only_the_visits_own_worker_may_tick()
    {
        using var h = new Harness();
        var id = await h.AssignAsync("Süpür");
        var item = h.Items(id)[0];

        Assert.IsType<NotFoundObjectResult>(
            await h.AsOtherWorker.SetChecklistItem(id, item.Id, new SetChecklistItemRequest(true)));
        // A manager tick would be worthless as evidence — refused on the same ownership gate.
        Assert.IsType<NotFoundObjectResult>(
            await h.AsManager.SetChecklistItem(id, item.Id, new SetChecklistItemRequest(true)));
        Assert.False(h.Items(id)[0].IsDone);
    }

    [Fact]
    public async Task An_item_from_another_visit_cannot_be_written_through_this_route()
    {
        using var h = new Harness();
        var a = await h.AssignAsync("A işi");
        var b = await h.AssignAsync("B işi");
        var foreignItem = h.Items(b)[0];

        Assert.IsType<NotFoundObjectResult>(
            await h.AsWorker.SetChecklistItem(a, foreignItem.Id, new SetChecklistItemRequest(true)));
        Assert.False(h.Items(b)[0].IsDone);
    }

    [Fact]
    public async Task Ticking_is_an_absolute_set_so_a_replay_is_a_no_op()
    {
        using var h = new Harness();
        var id = await h.AssignAsync("Süpür");
        var item = h.Items(id)[0];

        await h.AsWorker.SetChecklistItem(id, item.Id, new SetChecklistItemRequest(true));
        var firstStamp = h.Items(id)[0].DoneAtUtc;
        Assert.NotNull(firstStamp);

        await h.AsWorker.SetChecklistItem(id, item.Id, new SetChecklistItemRequest(true)); // replay
        Assert.Equal(firstStamp, h.Items(id)[0].DoneAtUtc);

        await h.AsWorker.SetChecklistItem(id, item.Id, new SetChecklistItemRequest(false));
        Assert.False(h.Items(id)[0].IsDone);
        Assert.Null(h.Items(id)[0].DoneAtUtc);
    }

    [Fact]
    public async Task Ticking_works_before_arrival_and_is_frozen_once_completed()
    {
        using var h = new Harness();
        var id = await h.AssignAsync("Süpür");
        var item = h.Items(id)[0];

        // Assigned — a "check in first" rule would be a block, so this is allowed.
        Assert.IsType<OkObjectResult>(await h.AsWorker.SetChecklistItem(id, item.Id, new SetChecklistItemRequest(true)));
        await h.CheckInAsync(id);
        Assert.IsType<OkObjectResult>(await h.AsWorker.SetChecklistItem(id, item.Id, new SetChecklistItemRequest(false)));

        await h.AsWorker.CheckOut(id, new FieldCheckOutRequest(40.4, 49.8));
        // Completed — evidence is frozen.
        var res = Assert.IsType<BadRequestObjectResult>(
            await h.AsWorker.SetChecklistItem(id, item.Id, new SetChecklistItemRequest(true)));
        Assert.Contains("VisitClosed", res.Value!.ToString());
    }

    // --- check-out: the pay-critical write, and what may never stop it -------------

    [Fact]
    public async Task Check_out_without_GPS_still_records_the_departure()
    {
        // The regression guard on the real bug: the app used to return early when GPS failed and post
        // NOTHING, so a worker in a basement could not record leaving and the day read as zero hours.
        using var h = new Harness();
        var id = await h.AssignAsync();
        await h.CheckInAsync(id);

        Assert.IsType<OkObjectResult>(await h.AsWorker.CheckOut(id, new FieldCheckOutRequest()));

        var v = h.Visit(id);
        Assert.Equal(FieldVisitStatus.Completed, v.Status);
        Assert.NotNull(v.CheckOutAtUtc);
        Assert.Null(v.CheckOutLatitude);   // unknown position — flagged, not refused
    }

    [Fact]
    public async Task A_retried_check_out_is_accepted_and_keeps_the_original_departure_time()
    {
        // The first response is easily lost — a 502 mid-deploy, an LTE/wifi handover — and the app
        // retries. Rejecting the retry would tell a worker who HAS clocked out that they failed to,
        // forever, and would strand the work photo that is sent right after this returns.
        using var h = new Harness();
        var id = await h.AssignAsync();
        await h.CheckInAsync(id);

        await h.AsWorker.CheckOut(id, new FieldCheckOutRequest(40.4, 49.8));
        var firstDeparture = h.Visit(id).CheckOutAtUtc;

        Assert.IsType<OkObjectResult>(await h.AsWorker.CheckOut(id, new FieldCheckOutRequest(40.4, 49.8)));
        Assert.Equal(firstDeparture, h.Visit(id).CheckOutAtUtc);   // not moved by the retry

        // Never arrived is still a genuine error — the two cases must not be conflated.
        var fresh = await h.AssignAsync();
        Assert.IsType<BadRequestObjectResult>(await h.AsWorker.CheckOut(fresh, new FieldCheckOutRequest()));
    }

    [Fact]
    public async Task Check_out_reconciles_the_final_tick_state()
    {
        using var h = new Harness();
        var id = await h.AssignAsync("A", "B", "C");
        await h.CheckInAsync(id);
        var items = h.Items(id);
        // B was ticked during the visit; the departure says A and C are the done ones.
        await h.AsWorker.SetChecklistItem(id, items[1].Id, new SetChecklistItemRequest(true));

        await h.AsWorker.CheckOut(id, new FieldCheckOutRequest(40.4, 49.8,
            DoneItemIds: new[] { items[0].Id, items[2].Id }));

        var after = h.Items(id);
        Assert.True(after[0].IsDone);
        Assert.False(after[1].IsDone);      // absolute set — B is unset, not left alone
        Assert.Null(after[1].DoneAtUtc);
        Assert.True(after[2].IsDone);
    }

    [Fact]
    public async Task Unticked_items_never_stop_a_check_out()
    {
        using var h = new Harness();
        var id = await h.AssignAsync("A", "B");
        await h.CheckInAsync(id);

        Assert.IsType<OkObjectResult>(await h.AsWorker.CheckOut(id, new FieldCheckOutRequest(40.4, 49.8)));
        Assert.Equal(FieldVisitStatus.Completed, h.Visit(id).Status);
        Assert.All(h.Items(id), i => Assert.False(i.IsDone));   // the gap travels to the manager, not a block
    }

    // --- work photo ---------------------------------------------------------------

    [Fact]
    public async Task A_failed_work_photo_upload_costs_the_worker_nothing()
    {
        using var h = new Harness();
        var id = await h.AssignAsync();
        await h.CheckInAsync(id);
        await h.AsWorker.CheckOut(id, new FieldCheckOutRequest(40.4, 49.8));
        h.Photo.ThrowOnWorkUpload = true;

        var ok = Assert.IsType<OkObjectResult>(await h.AsWorker.UploadWorkPhoto(id, new WorkPhotoRequest(TinyJpeg)));

        // Always 200, and `stored` tells the truth so the app can offer a retry instead of lying.
        Assert.Contains("stored = False", ok.Value!.ToString());
        var v = h.Visit(id);
        Assert.Null(v.WorkPhotoKey);
        Assert.Equal(FieldVisitStatus.Completed, v.Status);   // the departure still stands
    }

    [Fact]
    public async Task Uploading_the_work_photo_twice_stores_it_once()
    {
        using var h = new Harness();
        var id = await h.AssignAsync();
        await h.CheckInAsync(id);
        await h.AsWorker.CheckOut(id, new FieldCheckOutRequest(40.4, 49.8));

        await h.AsWorker.UploadWorkPhoto(id, new WorkPhotoRequest(TinyJpeg));
        await h.AsWorker.UploadWorkPhoto(id, new WorkPhotoRequest(TinyJpeg));   // retry of an unseen 200

        Assert.Equal(1, h.Photo.WorkUploads);
        Assert.StartsWith("fieldwork/", h.Visit(id).WorkPhotoKey);   // never the selfie prefix
    }

    [Fact]
    public async Task A_work_photo_cannot_be_attached_to_someone_elses_visit()
    {
        using var h = new Harness();
        var id = await h.AssignAsync();
        await h.CheckInAsync(id);

        Assert.IsType<NotFoundObjectResult>(await h.AsOtherWorker.UploadWorkPhoto(id, new WorkPhotoRequest(TinyJpeg)));
        Assert.Equal(0, h.Photo.WorkUploads);
    }

    [Fact]
    public async Task A_manager_reads_the_work_photo_but_out_of_scope_is_refused()
    {
        using var h = new Harness();
        var id = await h.AssignAsync();
        await h.CheckInAsync(id);
        await h.AsWorker.CheckOut(id, new FieldCheckOutRequest(40.4, 49.8));
        await h.AsWorker.UploadWorkPhoto(id, new WorkPhotoRequest(TinyJpeg));

        var ok = Assert.IsType<OkObjectResult>(await h.AsManager.WorkPhoto(id));
        Assert.Contains("fieldwork/", ok.Value!.ToString());

        // A worker has no read surface for it at all — the endpoint is Admin/Manager only, and the
        // manager's reach is the same Role==Employee-in-my-branches rule as everywhere else.
        h.Db.Employees.Single(e => e.Id == h.WorkerId).LocationId = Guid.NewGuid();   // moved away
        h.Db.SaveChanges();
        var denied = Assert.IsType<ObjectResult>(await h.AsManager.WorkPhoto(id));
        Assert.Equal(StatusCodes.Status403Forbidden, denied.StatusCode);
    }

    // --- tenancy ------------------------------------------------------------------

    [Fact]
    public async Task Checklist_items_are_invisible_to_another_tenant()
    {
        // The query-filter registration is the easy thing to forget when adding an entity.
        using var h = new Harness();
        var id = await h.AssignAsync("Süpür", "Sula");
        Assert.Equal(2, h.Items(id).Count);

        // Stamped to the caller's tenant automatically (the entity is in the tenantScoped list)…
        Assert.All(h.Items(id), i => Assert.Equal(TenantA, i.TenantId));
        Assert.Empty(await h.Db.FieldVisitChecklistItems
            .IgnoreQueryFilters().Where(i => i.TenantId == TenantB).ToListAsync());

        // …and a row planted under another tenant is invisible through the normal (filtered) query,
        // which is what the HasQueryFilter registration buys.
        h.Db.FieldVisitChecklistItems.Add(new FieldVisitChecklistItem
        {
            TenantId = TenantB, FieldVisitId = id, Label = "Yad tenant", SortOrder = 99,
        });
        await h.Db.SaveChangesAsync();
        Assert.DoesNotContain(await h.Db.FieldVisitChecklistItems.ToListAsync(), i => i.TenantId == TenantB);
    }
}
