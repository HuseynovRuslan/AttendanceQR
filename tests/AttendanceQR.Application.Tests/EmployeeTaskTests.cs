using System.Security.Claims;
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
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// «Tapşırıqlar» — a manager assigns, the worker presses «Hazırdır», the manager accepts or sends it
/// back. What these pin is the part that would quietly do harm: WHO may assign to whom, who may
/// declare work finished, and the house rule that nothing optional can block the record.
///
/// A branch manager must never reach another branch's staff, an admin, or a fellow manager — and a
/// worker must never be able to mark someone else's task done, because that record is a claim about
/// a person who never said it.
/// </summary>
public class EmployeeTaskTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000f6");

    private sealed class StubPhoto : IPhotoStorageService
    {
        public bool Throw { get; set; }
        public int TaskUploads { get; private set; }
        public Task<string> UploadTaskPhotoAsync(Guid t, Guid id, byte[] b, CancellationToken ct = default)
        {
            if (Throw) throw new InvalidOperationException("R2 down");
            TaskUploads++;
            return Task.FromResult($"tasks/{t}/{id}.jpg");
        }
        public Task<string> UploadCheckInPhotoAsync(Guid e, Guid r, byte[] b, CancellationToken ct = default) => Task.FromResult("checkins/k.jpg");
        public Task<string> UploadReferencePhotoAsync(Guid e, byte[] b, CancellationToken ct = default) => Task.FromResult("reference/k.jpg");
        public Task<string> UploadFieldWorkPhotoAsync(Guid t, Guid v, byte[] b, CancellationToken ct = default) => Task.FromResult("fieldwork/k.jpg");
        public Task<string> GetPresignedUrlAsync(string key, CancellationToken ct = default) => Task.FromResult($"https://r2/{key}");
        public Task<byte[]> GetBytesAsync(string key, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
        public Task DeleteByPrefixOlderThanAsync(string p, DateTime o, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> DeleteObjectsAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default) => Task.FromResult(keys.Count);
    }

    private sealed class StubPush : IPushNotifier
    {
        public bool Throw { get; set; }
        public List<(Guid To, string Title)> Sent { get; } = new();
        public Task<int> NotifyEmployeesAsync(IReadOnlyCollection<Guid> ids, string title, string body, string? url, CancellationToken ct = default)
        {
            if (Throw) throw new InvalidOperationException("push broker down");
            foreach (var id in ids) Sent.Add((id, title));
            return Task.FromResult(ids.Count);
        }
    }

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public StubPhoto Photo { get; } = new();
        public StubPush Push { get; } = new();
        public Guid BranchA { get; } = Guid.NewGuid();
        public Guid BranchB { get; } = Guid.NewGuid();
        public Guid ManagerA { get; } = Guid.NewGuid();
        public Guid WorkerA { get; } = Guid.NewGuid();
        public Guid WorkerB { get; } = Guid.NewGuid();
        public Guid AdminId { get; } = Guid.NewGuid();
        public Guid ManagerB { get; } = Guid.NewGuid();

        public Harness()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"tasks-{Guid.NewGuid()}").Options, tenant);

            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "T", Slug = "t", DisplayName = "T", IsActive = true });
            Db.Locations.Add(Branch(BranchA, "Filial A"));
            Db.Locations.Add(Branch(BranchB, "Filial B"));
            Db.ManagedLocations.Add(new ManagedLocation { EmployeeId = ManagerA, LocationId = BranchA, TenantId = TenantId });
            Db.ManagedLocations.Add(new ManagedLocation { EmployeeId = ManagerB, LocationId = BranchB, TenantId = TenantId });
            Db.Employees.Add(Person(ManagerA, "Menecer A", EmployeeRole.Manager, BranchA));
            Db.Employees.Add(Person(ManagerB, "Menecer B", EmployeeRole.Manager, BranchB));
            Db.Employees.Add(Person(WorkerA, "İşçi A", EmployeeRole.Employee, BranchA));
            Db.Employees.Add(Person(WorkerB, "İşçi B", EmployeeRole.Employee, BranchB));
            Db.Employees.Add(Person(AdminId, "Admin", EmployeeRole.Admin, BranchA));
            Db.SaveChanges();
        }

        private Location Branch(Guid id, string name) => new()
        {
            Id = id, TenantId = TenantId, Name = name, Latitude = 40.4, Longitude = 49.8,
            RadiusMeters = 150, ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
            LateThresholdMinutes = 15, QrVersion = 1, IsActive = true,
        };

        private Employee Person(Guid id, string name, EmployeeRole role, Guid locationId) => new()
        {
            Id = id, TenantId = TenantId, FullName = name, Email = $"{id:N}@t.local",
            LocationId = locationId, Role = role, IsActive = true,
            ActivatedAtUtc = DateTime.UtcNow, PasswordHash = "x",
        };

        public EmployeeTasksController As(Guid who, EmployeeRole role) =>
            new(Db, Photo, Push, new AppOptions { TimeZone = "Asia/Baku" }, NullLogger<EmployeeTasksController>.Instance)
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

        public EmployeeTask Seed(Guid employeeId, EmployeeTaskStatus status = EmployeeTaskStatus.Assigned, Guid? by = null)
        {
            var task = new EmployeeTask
            {
                TenantId = TenantId, EmployeeId = employeeId, AssignedByEmployeeId = by ?? ManagerA,
                Title = "Üçüncü mərtəbəni yığışdır", Status = status,
                DoneAtUtc = status is EmployeeTaskStatus.Done or EmployeeTaskStatus.Approved ? DateTime.UtcNow : null,
            };
            Db.EmployeeTasks.Add(task);
            Db.SaveChanges();
            return task;
        }

        public void Dispose() => Db.Dispose();
    }

    private static int Status(IActionResult r) => r switch
    {
        ObjectResult o => o.StatusCode ?? StatusCodes.Status200OK,
        _ => 0,
    };

    private static string? Error(IActionResult r) =>
        (r as ObjectResult)?.Value?.GetType().GetProperty("error")?.GetValue((r as ObjectResult)!.Value) as string;

    // --- who may assign to whom ----------------------------------------------

    [Fact]
    public async Task A_manager_assigns_to_their_own_branch_worker()
    {
        using var h = new Harness();
        var result = await h.As(h.ManagerA, EmployeeRole.Manager)
            .Assign(new AssignTaskRequest(h.WorkerA, "Üçüncü mərtəbəni yığışdır", null, null));

        Assert.IsType<OkObjectResult>(result);
        Assert.Single(h.Db.EmployeeTasks);
        // The worker is told, on the screen that shows it.
        Assert.Contains(h.Push.Sent, s => s.To == h.WorkerA && s.Title == "Yeni tapşırıq");
    }

    [Fact]
    public async Task A_manager_cannot_assign_across_branches()
    {
        using var h = new Harness();
        var result = await h.As(h.ManagerA, EmployeeRole.Manager)
            .Assign(new AssignTaskRequest(h.WorkerB, "Başqa filialın işi", null, null));

        Assert.Equal(StatusCodes.Status403Forbidden, Status(result));
        Assert.Empty(h.Db.EmployeeTasks);
    }

    [Theory]
    [InlineData(true)]  // to an admin
    [InlineData(false)] // to a fellow manager
    public async Task A_manager_cannot_assign_to_staff_they_do_not_manage(bool toAdmin)
    {
        // The management rule, not the looser visibility one: same branch is not enough — the target
        // must be a plain Employee. Assigning work to your own admin is not a manager's to do.
        using var h = new Harness();
        var target = toAdmin ? h.AdminId : h.ManagerB;

        var result = await h.As(h.ManagerA, EmployeeRole.Manager)
            .Assign(new AssignTaskRequest(target, "İş", null, null));

        Assert.Equal(StatusCodes.Status403Forbidden, Status(result));
    }

    [Fact]
    public async Task An_empty_title_is_refused_before_anything_is_written()
    {
        using var h = new Harness();
        var result = await h.As(h.ManagerA, EmployeeRole.Manager)
            .Assign(new AssignTaskRequest(h.WorkerA, "   ", null, null));

        Assert.Equal("TitleRequired", Error(result));
        Assert.Empty(h.Db.EmployeeTasks);
    }

    // --- who may declare it done ---------------------------------------------

    [Fact]
    public async Task Only_the_tasks_own_worker_can_press_hazirdir()
    {
        using var h = new Harness();
        var task = h.Seed(h.WorkerA);

        var byOther = await h.As(h.WorkerB, EmployeeRole.Employee).Complete(task.Id, new CompleteTaskRequest(null, null));
        // Not 403 but 404: the other worker must not even learn the task exists.
        Assert.IsType<NotFoundObjectResult>(byOther);

        var byManager = await h.As(h.ManagerA, EmployeeRole.Manager).Complete(task.Id, new CompleteTaskRequest(null, null));
        Assert.IsType<NotFoundObjectResult>(byManager);

        Assert.Equal(EmployeeTaskStatus.Assigned, (await h.Db.EmployeeTasks.FirstAsync()).Status);
    }

    [Fact]
    public async Task Pressing_hazirdir_twice_keeps_the_first_time()
    {
        // The reply to the first call is easily lost; the app retries. Refusing would tell a worker
        // who HAS finished that they had not.
        using var h = new Harness();
        var task = h.Seed(h.WorkerA);
        var worker = h.As(h.WorkerA, EmployeeRole.Employee);

        await worker.Complete(task.Id, new CompleteTaskRequest("bitdi", null));
        var first = (await h.Db.EmployeeTasks.FirstAsync()).DoneAtUtc;
        var again = await worker.Complete(task.Id, new CompleteTaskRequest("bitdi", null));

        Assert.IsType<OkObjectResult>(again);
        Assert.Equal(first, (await h.Db.EmployeeTasks.FirstAsync()).DoneAtUtc);
    }

    // --- the house rule: nothing optional blocks the record -------------------

    [Fact]
    public async Task A_failed_photo_upload_still_leaves_the_task_done()
    {
        using var h = new Harness();
        h.Photo.Throw = true;
        var task = h.Seed(h.WorkerA);

        var result = await h.As(h.WorkerA, EmployeeRole.Employee)
            .Complete(task.Id, new CompleteTaskRequest("bitdi", Convert.ToBase64String(new byte[64])));

        Assert.IsType<OkObjectResult>(result);
        var stored = await h.Db.EmployeeTasks.FirstAsync();
        Assert.Equal(EmployeeTaskStatus.Done, stored.Status);
        Assert.Null(stored.PhotoKey);
    }

    [Fact]
    public async Task A_broken_push_still_leaves_the_task_done()
    {
        using var h = new Harness();
        h.Push.Throw = true;
        var task = h.Seed(h.WorkerA);

        var result = await h.As(h.WorkerA, EmployeeRole.Employee).Complete(task.Id, new CompleteTaskRequest(null, null));

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(EmployeeTaskStatus.Done, (await h.Db.EmployeeTasks.FirstAsync()).Status);
    }

    // --- approve / send back --------------------------------------------------

    [Fact]
    public async Task Approving_needs_a_done_task_and_records_who_accepted_it()
    {
        using var h = new Harness();
        var task = h.Seed(h.WorkerA);
        var manager = h.As(h.ManagerA, EmployeeRole.Manager);

        var tooEarly = await manager.Approve(task.Id);
        Assert.Equal("NotDone", Error(tooEarly));

        await h.As(h.WorkerA, EmployeeRole.Employee).Complete(task.Id, new CompleteTaskRequest(null, null));
        Assert.IsType<OkObjectResult>(await manager.Approve(task.Id));

        var stored = await h.Db.EmployeeTasks.FirstAsync();
        Assert.Equal(EmployeeTaskStatus.Approved, stored.Status);
        Assert.Equal(h.ManagerA, stored.ApprovedByEmployeeId);
    }

    [Fact]
    public async Task Sending_it_back_reopens_it_and_tells_the_worker_why()
    {
        using var h = new Harness();
        var task = h.Seed(h.WorkerA, EmployeeTaskStatus.Done);

        var result = await h.As(h.ManagerA, EmployeeRole.Manager)
            .Reject(task.Id, new RejectTaskRequest("Künclər yığışdırılmayıb"));

        Assert.IsType<OkObjectResult>(result);
        var stored = await h.Db.EmployeeTasks.FirstAsync();
        Assert.Equal(EmployeeTaskStatus.Assigned, stored.Status);
        Assert.Null(stored.DoneAtUtc);
        Assert.Equal("Künclər yığışdırılmayıb", stored.RejectionNote);
        Assert.Contains(h.Push.Sent, s => s.To == h.WorkerA && s.Title == "Tapşırıq geri qaytarıldı");
    }

    [Fact]
    public async Task Redoing_a_returned_task_clears_the_old_complaint()
    {
        using var h = new Harness();
        var task = h.Seed(h.WorkerA, EmployeeTaskStatus.Done);
        await h.As(h.ManagerA, EmployeeRole.Manager).Reject(task.Id, new RejectTaskRequest("Yenidən et"));

        await h.As(h.WorkerA, EmployeeRole.Employee).Complete(task.Id, new CompleteTaskRequest("düzəltdim", null));

        var stored = await h.Db.EmployeeTasks.FirstAsync();
        Assert.Equal(EmployeeTaskStatus.Done, stored.Status);
        Assert.Null(stored.RejectionNote);
    }

    [Fact]
    public async Task A_manager_cannot_approve_another_branchs_task()
    {
        using var h = new Harness();
        var task = h.Seed(h.WorkerB, EmployeeTaskStatus.Done, by: h.ManagerB);

        var result = await h.As(h.ManagerA, EmployeeRole.Manager).Approve(task.Id);

        Assert.Equal(StatusCodes.Status403Forbidden, Status(result));
    }

    // --- the worker's own list ------------------------------------------------

    [Fact]
    public async Task The_worker_sees_only_their_own_open_work_and_never_a_cancelled_job()
    {
        using var h = new Harness();
        h.Seed(h.WorkerA);
        h.Seed(h.WorkerA, EmployeeTaskStatus.Cancelled);
        h.Seed(h.WorkerB); // somebody else's

        var result = await h.As(h.WorkerA, EmployeeRole.Employee).Mine();

        var items = Assert.IsAssignableFrom<IEnumerable<object>>(((OkObjectResult)result).Value);
        Assert.Single(items);
    }
}
