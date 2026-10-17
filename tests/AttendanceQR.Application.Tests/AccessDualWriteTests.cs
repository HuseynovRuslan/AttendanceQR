using System.Security.Claims;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Application.Common;
using AttendanceQR.Domain.Access;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Security;
using AttendanceQR.Infrastructure.Services;
using AttendanceQR.Infrastructure.Services.Access;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The employee form is where today's access changes — a person's role, a manager's branches — and the
/// permission model hears of it in the same request (AccessSeeder.ReconcileAfterWriteAsync), not at the next
/// start-up. Until 2026-10-15 nothing tested this path end to end: the reconcile itself was tested, the
/// controller calling it was not.
/// </summary>
public class AccessDualWriteTests
{
    private static AdminController Form(AccessWorld w, AppDbContext db) =>
        new(db, Options.Create(new InvitationOptions()), new PasswordHasher(),
            new MemoryCacheLoginLockoutStore(new MemoryCache(new MemoryCacheOptions())),
            w.Options, new NoPhotos(), NullLogger<AdminController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim("sub", w.Admin1.ToString()), new Claim("role", "Admin")], "test")),
                },
            },
        };

    /// <summary>The form as the screen sends it: everything the person has, with the change on top.</summary>
    private static async Task<IActionResult> SaveAsync(
        AccessWorld w, Guid employeeId, EmployeeRole role, IReadOnlyList<Guid>? managedLocationIds)
    {
        var db = w.Context(AccessWorld.TenantA);
        var e = db.Employees.Single(x => x.Id == employeeId);
        return await Form(w, db).Update(employeeId, new EmployeeUpdateRequest(
            e.FullName, e.Email, e.LocationId, role, e.IsActive, e.PhoneNumber,
            ManagedLocationIds: managedLocationIds));
    }

    private static async Task<EffectiveAccess> AccessOfAsync(AccessWorld w, Guid employeeId) =>
        (await AccessGraph.LoadAsync(w.Context(AccessWorld.TenantA))).For(employeeId);

    [Fact]
    public async Task A_managers_new_branches_reach_the_permission_model_in_the_same_request()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();

        Assert.IsType<OkObjectResult>(await SaveAsync(w, w.M3, EmployeeRole.Manager, [w.B3, w.B5]));

        var branches = (await AccessOfAsync(w, w.M3)).BranchesFor(AccessPermissions.EmployeesView);
        Assert.True(branches!.SetEquals([w.B3, w.B5]));
    }

    [Fact]
    public async Task A_manager_made_admin_on_the_form_is_company_administrator_at_once_and_nothing_else()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();

        Assert.IsType<OkObjectResult>(await SaveAsync(w, w.M3, EmployeeRole.Admin, null));

        var access = await AccessOfAsync(w, w.M3);
        Assert.Equal([AccessRoleTemplates.CompanyAdmin], access.Grants.Select(g => g.TemplateKey));
        Assert.Null(access.BranchesFor(AccessPermissions.EmployeesView)); // company-wide
    }

    [Fact]
    public async Task A_manager_made_plain_staff_on_the_form_loses_everything_at_once()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();

        Assert.IsType<OkObjectResult>(await SaveAsync(w, w.M2, EmployeeRole.Employee, null));

        Assert.Empty((await AccessOfAsync(w, w.M2)).Grants);
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
}
