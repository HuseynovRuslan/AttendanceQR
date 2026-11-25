using System.Security.Claims;
using System.Text.Json;
using AttendanceQR.Api;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Application.Common;
using AttendanceQR.Domain.Access;
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
/// Who made somebody an admin?
///
/// The employee form changed Employee.Role in place and wrote nothing. AccessSeeder then mirrored the new role
/// as a system row with no author, so the only trace a promotion left was the minute that row was made. Every
/// path that gives, changes or takes a role now writes one EmployeeRoleChanged row — by whom, to whom, the role
/// and the access before and after, through which route and screen — in the same save as the change.
///
/// Pinned here: the row is written for a real change and only for one; it carries the before and after; a
/// support session names the operator behind it; a refused change leaves neither the change nor a row; and no
/// credential ever reaches the log.
/// </summary>
public class RoleChangeAuditTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000a7");

    private sealed class World : IDisposable
    {
        public AppDbContext Db { get; }
        public AdminController Admin { get; }
        public Guid ActorId { get; } = Guid.NewGuid();
        public Guid BranchA { get; }
        public Guid BranchB { get; }
        private int _phone = 1000;

        public World(Guid? operatorBehind = null)
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"role-audit-{Guid.NewGuid()}").Options,
                tenant);
            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "T", Slug = "t", DisplayName = "T", IsActive = true });
            BranchA = Branch("Filial A");
            BranchB = Branch("Filial B");
            Db.Employees.Add(new Employee
            {
                Id = ActorId, TenantId = TenantId, FullName = "Şirkət Admini", Role = EmployeeRole.Admin,
                IsActive = true, PasswordHash = "h", LocationId = BranchA, ActivatedAtUtc = DateTime.UtcNow,
                PhoneNumber = "+994500000001",
            });
            Db.SaveChanges();

            var claims = new List<Claim>
            {
                new("sub", ActorId.ToString()),
                new("role", nameof(EmployeeRole.Admin)),
            };
            if (operatorBehind is Guid op)
                claims.Add(new Claim("imp", op.ToString()));

            Admin = new AdminController(
                Db,
                Options.Create(new InvitationOptions()),
                new PasswordHasher(),
                new MemoryCacheLoginLockoutStore(new MemoryCache(new MemoryCacheOptions())),
                new AppOptions { TimeZone = "Asia/Baku" },
                new NoPhotos(),
                NullLogger<AdminController>.Instance)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) },
                },
            };
        }

        private Guid Branch(string name)
        {
            var location = new Location
            {
                TenantId = TenantId, Name = name, Latitude = 40.4, Longitude = 49.8, RadiusMeters = 150,
                ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0), IsActive = true,
            };
            Db.Locations.Add(location);
            return location.Id;
        }

        public Guid Person(EmployeeRole role, params Guid[] managed)
        {
            var id = Guid.NewGuid();
            Db.Employees.Add(new Employee
            {
                Id = id, TenantId = TenantId, FullName = "Məmmədov Əli", Role = role, IsActive = true,
                PasswordHash = "secret-pin-hash", LocationId = BranchA, ActivatedAtUtc = DateTime.UtcNow,
                PhoneNumber = $"+99450123{_phone++}", Email = $"ali{_phone}@example.az",
            });
            foreach (var branch in managed)
                Db.ManagedLocations.Add(new ManagedLocation { EmployeeId = id, LocationId = branch, TenantId = TenantId });
            Db.SaveChanges();
            Db.ChangeTracker.Clear();
            return id;
        }

        /// <summary>The form as the admin panel sends it: every field as it stands, with the ones under test changed.</summary>
        public EmployeeUpdateRequest Edit(Guid id, EmployeeRole role, IReadOnlyList<Guid>? managed = null, string? fullName = null)
        {
            var e = Db.Employees.AsNoTracking().Single(x => x.Id == id);
            return new EmployeeUpdateRequest(
                FullName: fullName ?? e.FullName, Email: e.Email, LocationId: e.LocationId, Role: role, IsActive: e.IsActive,
                PhoneNumber: e.PhoneNumber, ManagedLocationIds: managed);
        }

        public List<AuditLog> Rows() => Db.AuditLogs.AsNoTracking()
            .Where(a => a.EventType == AuditEventType.EmployeeRoleChanged)
            .ToList();

        public EmployeeRole RoleOf(Guid id) => Db.Employees.AsNoTracking().Single(e => e.Id == id).Role;

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

    private static JsonElement Body(AuditLog row) => JsonDocument.Parse(row.Reason!).RootElement;

    private static List<string> Templates(JsonElement side) =>
        side.GetProperty("grants").EnumerateArray().Select(g => g.GetProperty("template").GetString()!).ToList();

    private static List<Guid> Branches(JsonElement side, string template) =>
        side.GetProperty("grants").EnumerateArray()
            .Where(g => g.GetProperty("template").GetString() == template)
            .SelectMany(g => g.GetProperty("branches").EnumerateArray().Select(b => b.GetGuid()))
            .ToList();

    // --- the employee form ---------------------------------------------------------------

    [Fact]
    public async Task Promoting_to_admin_writes_who_whom_before_after_when_and_where()
    {
        using var w = new World();
        var target = w.Person(EmployeeRole.Employee);

        var result = await w.Admin.Update(target, w.Edit(target, EmployeeRole.Admin));

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(EmployeeRole.Admin, w.RoleOf(target));
        var row = Assert.Single(w.Rows());
        Assert.Equal(target, row.EmployeeId);
        Assert.Equal(TenantId, row.TenantId);
        Assert.True(row.CreatedAtUtc > DateTime.UtcNow.AddMinutes(-1));

        var body = Body(row);
        Assert.Equal("changed", body.GetProperty("action").GetString());
        Assert.Equal(w.ActorId, body.GetProperty("by").GetGuid());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("operator").ValueKind);
        Assert.Equal(target, body.GetProperty("target").GetGuid());
        Assert.Equal(TenantId, body.GetProperty("tenant").GetGuid());
        Assert.Equal(RoleChangeAudit.Via.AdminEmployeeEdit, body.GetProperty("via").GetString());
        Assert.Equal("Employee", body.GetProperty("from").GetProperty("role").GetString());
        Assert.Empty(Templates(body.GetProperty("from")));
        Assert.Equal("Admin", body.GetProperty("to").GetProperty("role").GetString());
        Assert.Equal(new[] { AccessRoleTemplates.CompanyAdmin }, Templates(body.GetProperty("to")));
        var permissions = body.GetProperty("to").GetProperty("grants")[0].GetProperty("permissions")
            .EnumerateArray().Select(p => p.GetString()).ToList();
        Assert.Equal(AccessRoleTemplates.CompanyAdminTemplate.Permissions.Count, permissions.Count);
        Assert.Contains(AccessPermissions.EmployeesUpdate, permissions);
    }

    [Fact]
    public async Task A_managers_new_branches_are_recorded_with_the_old_ones()
    {
        using var w = new World();
        var manager = w.Person(EmployeeRole.Manager, w.BranchA);

        var result = await w.Admin.Update(manager, w.Edit(manager, EmployeeRole.Manager, [w.BranchA, w.BranchB]));

        Assert.IsType<OkObjectResult>(result);
        var body = Body(Assert.Single(w.Rows()));
        Assert.Equal("Manager", body.GetProperty("from").GetProperty("role").GetString());
        Assert.Equal("Manager", body.GetProperty("to").GetProperty("role").GetString());
        Assert.Equal(new[] { w.BranchA }, Branches(body.GetProperty("from"), AccessRoleTemplates.LegacyManager));
        Assert.Equal(new[] { w.BranchA, w.BranchB }.Order(), Branches(body.GetProperty("to"), AccessRoleTemplates.LegacyManager));
        Assert.Contains(AccessRoleTemplates.PinHelper, Templates(body.GetProperty("to")));
    }

    [Fact]
    public async Task Demoting_a_manager_records_the_branches_they_lose()
    {
        using var w = new World();
        var manager = w.Person(EmployeeRole.Manager, w.BranchA, w.BranchB);

        await w.Admin.Update(manager, w.Edit(manager, EmployeeRole.Employee));

        var body = Body(Assert.Single(w.Rows()));
        Assert.Equal(2, Branches(body.GetProperty("from"), AccessRoleTemplates.LegacyManager).Count);
        Assert.Equal("Employee", body.GetProperty("to").GetProperty("role").GetString());
        Assert.Empty(Templates(body.GetProperty("to")));
    }

    [Fact]
    public async Task An_ordinary_edit_writes_nothing()
    {
        using var w = new World();
        var employee = w.Person(EmployeeRole.Employee);
        var manager = w.Person(EmployeeRole.Manager, w.BranchA);

        await w.Admin.Update(employee, w.Edit(employee, EmployeeRole.Employee, fullName: "Yeni Ad"));
        // A manager edited without the branch list keeps their branches: nothing about their access moved.
        await w.Admin.Update(manager, w.Edit(manager, EmployeeRole.Manager, managed: null, fullName: "Başqa Ad"));
        // The same branches sent back in another order are the same branches.
        await w.Admin.Update(manager, w.Edit(manager, EmployeeRole.Manager, [w.BranchA]));

        Assert.Empty(w.Rows());
    }

    [Fact]
    public async Task A_refused_change_leaves_neither_the_change_nor_a_row()
    {
        using var w = new World();
        var employee = w.Person(EmployeeRole.Employee);

        // Promoted to Manager over a branch that does not exist: refused after the role was set in memory.
        var result = await w.Admin.Update(employee, w.Edit(employee, EmployeeRole.Manager, [Guid.NewGuid()]));

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(EmployeeRole.Employee, w.RoleOf(employee));
        Assert.Empty(w.Rows());
    }

    [Fact]
    public void The_row_joins_the_callers_save_instead_of_saving_itself()
    {
        using var w = new World();
        var target = w.Person(EmployeeRole.Employee);

        var row = RoleChangeAudit.Add(w.Db, w.Admin.User, null, TenantId, target, RoleChangeAudit.Actions.Changed,
            EmployeeRole.Employee, [], EmployeeRole.Manager, [w.BranchA], RoleChangeAudit.Via.AdminEmployeeEdit);

        Assert.Equal(EntityState.Added, w.Db.Entry(row).State);
        Assert.Empty(w.Rows());           // nothing reached the store before the caller saves
        w.Db.SaveChanges();
        Assert.Single(w.Rows());
    }

    // --- a support session ---------------------------------------------------------------

    [Fact]
    public async Task A_support_session_names_the_operator_behind_the_borrowed_admin()
    {
        var operatorId = Guid.NewGuid();
        using var w = new World(operatorBehind: operatorId);
        var target = w.Person(EmployeeRole.Employee);

        // Promotion to Admin is refused in a support session; to Manager is not.
        var result = await w.Admin.Update(target, w.Edit(target, EmployeeRole.Manager, [w.BranchB]));

        Assert.IsType<OkObjectResult>(result);
        var body = Body(Assert.Single(w.Rows()));
        Assert.Equal(w.ActorId, body.GetProperty("by").GetGuid());
        Assert.Equal(operatorId, body.GetProperty("operator").GetGuid());
    }

    // --- accounts created with a role ----------------------------------------------------

    [Fact]
    public async Task An_account_created_as_manager_is_audited_and_plain_staff_is_not()
    {
        using var w = new World();

        var manager = await w.Admin.Invite(new InviteRequest(
            FullName: "Yeni Menecer", Email: null, LocationId: w.BranchA, Role: EmployeeRole.Manager,
            PhoneNumber: "+994551112233", ActivateWithPin: true));
        var staff = await w.Admin.Invite(new InviteRequest(
            FullName: "Yeni İşçi", Email: null, LocationId: w.BranchA, Role: EmployeeRole.Employee,
            PhoneNumber: "+994551112244", ActivateWithPin: true));

        Assert.IsType<OkObjectResult>(manager);
        Assert.IsType<OkObjectResult>(staff);
        var body = Body(Assert.Single(w.Rows()));
        Assert.Equal("created", body.GetProperty("action").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("from").GetProperty("role").ValueKind);
        Assert.Equal("Manager", body.GetProperty("to").GetProperty("role").GetString());
        Assert.Equal(RoleChangeAudit.Via.AdminInvite, body.GetProperty("via").GetString());
    }

    [Fact]
    public async Task A_bulk_import_audits_only_the_rows_above_employee()
    {
        using var w = new World();

        var result = await w.Admin.BulkImport(new BulkInviteRequest(w.BranchA, EmployeeRole.Employee,
        [
            new BulkInviteRow("Birinci İşçi", PhoneNumber: "+994552220001"),
            new BulkInviteRow("Filial Meneceri", PhoneNumber: "+994552220002", RoleName: "Menecer"),
            new BulkInviteRow("İkinci İşçi", PhoneNumber: "+994552220003"),
        ]));

        Assert.IsType<OkObjectResult>(result);
        var row = Assert.Single(w.Rows());
        var manager = w.Db.Employees.AsNoTracking().Single(e => e.FullName == "Filial Meneceri");
        Assert.Equal(EmployeeRole.Manager, manager.Role);
        Assert.Equal(manager.Id, row.EmployeeId);
        Assert.Equal(RoleChangeAudit.Via.AdminBulkImport, Body(row).GetProperty("via").GetString());
    }

    // --- never a secret ------------------------------------------------------------------

    [Fact]
    public async Task No_pin_token_phone_or_email_reaches_the_log()
    {
        using var w = new World();
        var invited = await w.Admin.Invite(new InviteRequest(
            FullName: "Gizli Admin", Email: "gizli@example.az", LocationId: w.BranchA, Role: EmployeeRole.Manager,
            PhoneNumber: "+994557778899", ActivateWithPin: true));
        var linked = await w.Admin.Invite(new InviteRequest(
            FullName: "Linkli Menecer", Email: null, LocationId: w.BranchA, Role: EmployeeRole.Manager,
            PhoneNumber: "+994557778800"));
        var promoted = w.Person(EmployeeRole.Employee);
        await w.Admin.Update(promoted, w.Edit(promoted, EmployeeRole.Admin));

        var tempPin = ((OkObjectResult)invited).Value!.GetType().GetProperty("tempPin")!.GetValue(((OkObjectResult)invited).Value)!.ToString()!;
        var token = ((OkObjectResult)linked).Value!.GetType().GetProperty("activationToken")!.GetValue(((OkObjectResult)linked).Value)!.ToString()!;
        var secrets = w.Db.Employees.AsNoTracking().ToList()
            .SelectMany(e => new[] { e.PasswordHash, e.PhoneNumber, e.Email, e.InvitationTokenHash })
            // Long enough to mean something: the actor's stand-in hash is a single letter.
            .Where(s => s is { Length: >= 6 })
            .Concat(new[] { tempPin, token, "557778899", "passwordHash", "invitationToken" })
            .ToList();

        var rows = w.Rows();
        Assert.Equal(3, rows.Count);
        foreach (var row in rows)
            foreach (var secret in secrets)
                Assert.DoesNotContain(secret!, row.Reason!, StringComparison.OrdinalIgnoreCase);
    }
}
