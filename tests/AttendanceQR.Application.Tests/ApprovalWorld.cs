using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using AttendanceQR.Api.Approvals;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Access;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// AccessWorld with two structures and the people of the approval flow, shaped like the owner's split of
/// 2026-11-11:
/// <list type="bullet">
/// <item>S1 («Aeroport yolu»): B1 and B2. M4 is its Operator, E3 its Observer, and E1 keeps B1's time.</item>
/// <item>S2 («Şəhər parkları»): B3. M5 is its Operator.</item>
/// <item>B4 and B5 are in no structure: they stay on today's flow whatever is switched on.</item>
/// </list>
/// M1 is today's branch manager of B1 and B2 (the legacy role), Admin1 and Admin2 are Company administrators,
/// and tenant B is the other company that must never leak in. Nothing is switched on until a test does it.
/// </summary>
internal sealed class ApprovalWorld : IDisposable
{
    public AccessWorld W { get; } = new();
    public Guid S1 { get; } = Guid.NewGuid();
    public Guid S2 { get; } = Guid.NewGuid();
    public FixedClock Clock { get; } = new(new DateTimeOffset(2026, 11, 11, 8, 0, 0, TimeSpan.Zero)); // 12:00 in Baku
    public ApprovalOptions On { get; } = new() { Enabled = true };

    public Guid Operator1 => W.M4;
    public Guid Operator2 => W.M5;
    public Guid Timekeeper => W.E1;
    public Guid Observer => W.E3;
    public Guid LegacyManager => W.M1;

    public static async Task<ApprovalWorld> CreateAsync()
    {
        var a = new ApprovalWorld();
        await a.W.ReconcileAndLoadAsync();
        using (var db = a.Db())
        {
            db.Structures.Add(new Structure { Id = a.S1, TenantId = AccessWorld.TenantA, Name = "Aeroport yolu" });
            db.Structures.Add(new Structure { Id = a.S2, TenantId = AccessWorld.TenantA, Name = "Şəhər parkları" });
            await db.SaveChangesAsync();
            foreach (var l in db.Locations.Where(l => l.Id == a.W.B1 || l.Id == a.W.B2))
                l.StructureId = a.S1;
            db.Locations.Single(l => l.Id == a.W.B3).StructureId = a.S2;
            await db.SaveChangesAsync();
        }
        await a.GiveAsync(a.Operator1, AccessRoleTemplates.StructureOperator, structure: a.S1);
        await a.GiveAsync(a.Operator2, AccessRoleTemplates.StructureOperator, structure: a.S2);
        await a.GiveAsync(a.Observer, AccessRoleTemplates.Observer, structure: a.S1);
        await a.GiveAsync(a.Timekeeper, AccessRoleTemplates.Timekeeper, branch: a.W.B1);
        return a;
    }

    public AppDbContext Db(Guid? tenant = null, IInterceptor? interceptor = null) =>
        W.Context(tenant ?? AccessWorld.TenantA, interceptor: interceptor);

    public ChangeRequestDesk Desk(ApprovalOptions? options = null, IInterceptor? interceptor = null, Guid? tenant = null) =>
        new(Db(tenant, interceptor), new Hasher(), options ?? On, Clock, NullLogger<ChangeRequestDesk>.Instance);

    public ApprovalAreaDesk Areas(Guid? tenant = null) => new(Db(tenant), Clock);

    public ManagerController ManagerDesk(Guid caller, ApprovalOptions? options = null) =>
        new(Db(), new Hasher(), new NoSummary(), W.Options, options ?? On)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim("sub", caller.ToString()), new Claim("role", "Manager")], "test")),
                },
            },
        };

    public ApprovalsController Api(Guid caller, ApprovalOptions? options = null)
    {
        var db = Db();
        var opts = options ?? On;
        return new ApprovalsController(
            new ChangeRequestDesk(db, new Hasher(), opts, Clock, NullLogger<ChangeRequestDesk>.Instance),
            new ApprovalAreaDesk(db, Clock))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim("sub", caller.ToString()), new Claim("role", "Employee")], "test")),
                },
            },
        };
    }

    public async Task GiveAsync(Guid employee, string template, Guid? structure = null, Guid? branch = null)
    {
        using var db = Db();
        var role = db.AccessRoles.Single(r => r.TemplateKey == template);
        db.RoleAssignments.Add(new RoleAssignment
        {
            TenantId = AccessWorld.TenantA, EmployeeId = employee, AccessRoleId = role.Id,
            Scope = structure is not null ? AccessScopeType.Structure : AccessScopeType.Branch,
            StructureId = structure, LocationId = branch, CreatedByEmployeeId = W.Admin1,
        });
        await db.SaveChangesAsync();
    }

    public async Task RevokeAsync(Guid employee, string template)
    {
        using var db = Db();
        var role = db.AccessRoles.Single(r => r.TemplateKey == template);
        foreach (var a in db.RoleAssignments.Where(a => a.EmployeeId == employee && a.AccessRoleId == role.Id && a.RevokedAtUtc == null))
            a.RevokedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task SetModeAsync(AuthzMode mode)
    {
        using var db = Db();
        db.Tenants.Single(t => t.Id == AccessWorld.TenantA).AuthzMode = mode;
        await db.SaveChangesAsync();
    }

    /// <summary>Switches an area on through the Company administrator's own endpoint, as the owner would.</summary>
    public async Task<Guid> ActivateAsync(Guid? structure = null, Guid? branch = null)
    {
        var result = await Areas().ActivateAsync(W.Admin1,
            new ApprovalAreaBody(structure is not null ? AccessScopeType.Structure : AccessScopeType.Branch, structure, branch),
            null, default);
        Assert.True(result.Status is 200 or 201, $"activation answered {result.Status}: {Json(result.Body)}");
        return Json(result.Body).GetProperty("id").GetGuid();
    }

    public static EmployeeCreateRequestBody Person(Guid branch, string phone = "551234567", string first = "Aysel",
        string last = "Məmmədova", Guid? schedule = null, string? position = null, string? email = null,
        string? note = "Yeni işçi, sabahdan başlayır") =>
        new(first, last, null, phone, email, position, null, branch, schedule, note);

    /// <summary>The manager desk's body for adding the same person directly.</summary>
    public static AttendanceQR.Api.Contracts.ManagerEmployeeRequest DeskPerson(
        Guid branch, string phone, string first = "Aysel", string last = "Məmmədova") =>
        new(FullName: $"{first} {last}", Email: null, PhoneNumber: phone, FatherName: null, Position: null,
            LocationId: branch, FirstName: first, LastName: last);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>A desk's or controller's answer, as the client reads it.</summary>
    public static JsonElement Json(object? body) => JsonSerializer.SerializeToElement(body, JsonOptions);

    public static (int Status, JsonElement Body) Read(IActionResult result) => result switch
    {
        ObjectResult o => (o.StatusCode ?? 200, Json(o.Value)),
        StatusCodeResult s => (s.StatusCode, default),
        _ => throw new InvalidOperationException(result.GetType().Name),
    };

    public static Guid IdOf(DeskResult result)
    {
        var body = Json(result.Body);
        return body.TryGetProperty("request", out var r) ? r.GetProperty("id").GetGuid() : body.GetProperty("id").GetGuid();
    }

    public ChangeRequest Request(Guid id) => Db().ChangeRequests.AsNoTracking().Single(r => r.Id == id);

    public List<ChangeRequestEventKind> History(Guid id) =>
        Db().ChangeRequestEvents.AsNoTracking().Where(e => e.ChangeRequestId == id)
            .OrderBy(e => e.AtUtc).ThenBy(e => e.Kind).Select(e => e.Kind).ToList();

    public List<Employee> PeopleWithPhone(string phone) =>
        Db().Employees.AsNoTracking().Where(e => e.PhoneNumber == phone).ToList();

    public void Dispose() => W.Dispose();

    internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Hasher : IPasswordHasher
    {
        public string Hash(string password) => "hashed:" + password;
        public bool Verify(string hash, string password) => hash == "hashed:" + password;
    }

    private sealed class NoSummary : IDailySummaryService
    {
        public Task<int> GenerateForDateAsync(DateOnly date, CancellationToken ct = default) => Task.FromResult(0);
    }
}
