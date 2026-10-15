using System.Security.Claims;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Application.Common;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Security;
using AttendanceQR.Infrastructure.Services.Access;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// A company shaped like Bakı Abadlıq's awkward cases, for the permission-model tests: managers who share a
/// branch, a manager whose own branch is not one they manage, one who manages everything, admins, plain
/// staff, and a platform operator's row — plus a second company that must never leak in.
/// </summary>
internal sealed class AccessWorld : IDisposable
{
    public static readonly Guid TenantA = Guid.Parse("00000000-0000-0000-0000-0000000ac0a1");
    public static readonly Guid TenantB = Guid.Parse("00000000-0000-0000-0000-0000000ac0b2");

    private readonly string _db = $"access-{Guid.NewGuid()}";
    private readonly List<AppDbContext> _open = [];

    public Guid B1 { get; } = Guid.NewGuid();
    public Guid B2 { get; } = Guid.NewGuid();
    public Guid B3 { get; } = Guid.NewGuid();
    public Guid B4 { get; } = Guid.NewGuid();
    public Guid B5 { get; } = Guid.NewGuid();
    public Guid OtherBranch { get; } = Guid.NewGuid();

    public Guid Admin1 { get; } = Guid.NewGuid();
    public Guid Admin2 { get; } = Guid.NewGuid();
    /// <summary>Manages B1, B2; works at B1.</summary>
    public Guid M1 { get; } = Guid.NewGuid();
    /// <summary>Manages B2; works at B2 — M1's colleague at B2.</summary>
    public Guid M2 { get; } = Guid.NewGuid();
    /// <summary>Manages B3 but works at B4 — today nobody who manages only B3 reaches them.</summary>
    public Guid M3 { get; } = Guid.NewGuid();
    /// <summary>Manages every branch; works at B5.</summary>
    public Guid M4 { get; } = Guid.NewGuid();
    /// <summary>Manages B2, B3; works at B1.</summary>
    public Guid M5 { get; } = Guid.NewGuid();
    public Guid E1 { get; } = Guid.NewGuid();
    public Guid E2 { get; } = Guid.NewGuid();
    public Guid E3 { get; } = Guid.NewGuid();
    public Guid E4 { get; } = Guid.NewGuid();
    public Guid E5 { get; } = Guid.NewGuid();
    /// <summary>A platform operator's row inside the company, as in bax.</summary>
    public Guid Operator { get; } = Guid.NewGuid();
    public Guid OtherCompanyManager { get; } = Guid.NewGuid();

    public AppOptions Options { get; }

    public AccessWorld()
    {
        Options = new AppOptions { SuperAdminEmployeeIds = Operator.ToString() };
        using var db = Context(TenantA);
        db.Tenants.Add(new Tenant { Id = TenantA, Name = "A", Slug = "a", DisplayName = "A", IsActive = true });
        db.Tenants.Add(new Tenant { Id = TenantB, Name = "B", Slug = "b", DisplayName = "B", IsActive = true });
        foreach (var (id, name) in new[] { (B1, "B1"), (B2, "B2"), (B3, "B3"), (B4, "B4"), (B5, "B5") })
            db.Locations.Add(new Location { Id = id, TenantId = TenantA, Name = name, IsActive = true, WorkDaysMask = 127, QrVersion = 1 });
        db.Locations.Add(new Location { Id = OtherBranch, TenantId = TenantB, Name = "X", IsActive = true, WorkDaysMask = 127, QrVersion = 1 });

        Person(db, Admin1, EmployeeRole.Admin, B1);
        Person(db, Admin2, EmployeeRole.Admin, B3);
        Person(db, M1, EmployeeRole.Manager, B1, B1, B2);
        Person(db, M2, EmployeeRole.Manager, B2, B2);
        Person(db, M3, EmployeeRole.Manager, B4, B3);
        Person(db, M4, EmployeeRole.Manager, B5, B1, B2, B3, B4, B5);
        Person(db, M5, EmployeeRole.Manager, B1, B2, B3);
        Person(db, E1, EmployeeRole.Employee, B1);
        Person(db, E2, EmployeeRole.Employee, B2);
        Person(db, E3, EmployeeRole.Employee, B3);
        Person(db, E4, EmployeeRole.Employee, B4);
        Person(db, E5, EmployeeRole.Employee, B5);
        Person(db, Operator, EmployeeRole.Employee, B2);
        db.Employees.Add(new Employee
        {
            Id = OtherCompanyManager, TenantId = TenantB, FullName = "Başqa şirkət", Email = $"{OtherCompanyManager:N}@b.local",
            Role = EmployeeRole.Manager, LocationId = OtherBranch, IsActive = true, PasswordHash = "hashed:1234",
            ActivatedAtUtc = DateTime.UtcNow.AddDays(-30),
        });
        db.ManagedLocations.Add(new ManagedLocation { TenantId = TenantB, EmployeeId = OtherCompanyManager, LocationId = OtherBranch });
        db.SaveChanges();
    }

    private static void Person(AppDbContext db, Guid id, EmployeeRole role, Guid home, params Guid[] managed)
    {
        db.Employees.Add(new Employee
        {
            Id = id, TenantId = TenantA, FullName = id.ToString()[..8], Email = $"{id:N}@a.local", PhoneNumber = Phone(id),
            Role = role, LocationId = home, IsActive = true, PasswordHash = "hashed:1234",
            ActivatedAtUtc = DateTime.UtcNow.AddDays(-30),
        });
        foreach (var branch in managed)
            db.ManagedLocations.Add(new ManagedLocation { TenantId = TenantA, EmployeeId = id, LocationId = branch });
    }

    private static string Phone(Guid id) => "50" + Math.Abs(id.GetHashCode() % 10_000_000).ToString("D7");

    public IReadOnlyList<Guid> Panel => [Admin1, Admin2, M1, M2, M3, M4, M5];

    public string Name(Guid id) =>
        id == Admin1 ? "Admin1" : id == Admin2 ? "Admin2" : id == M1 ? "M1" : id == M2 ? "M2" : id == M3 ? "M3"
        : id == M4 ? "M4" : id == M5 ? "M5" : id == E1 ? "E1" : id == E2 ? "E2" : id == E3 ? "E3" : id == E4 ? "E4"
        : id == E5 ? "E5" : id == Operator ? "Operator" : id == OtherCompanyManager ? "OtherCompanyManager" : id.ToString()[..8];
    public IReadOnlyList<Guid> Everyone => [Admin1, Admin2, M1, M2, M3, M4, M5, E1, E2, E3, E4, E5, Operator];

    public EmployeeRole RoleOf(Guid id) =>
        id == Admin1 || id == Admin2 ? EmployeeRole.Admin
        : id == M1 || id == M2 || id == M3 || id == M4 || id == M5 ? EmployeeRole.Manager
        : EmployeeRole.Employee;

    public AppDbContext Context(Guid tenantId, IAuthzObserver? observer = null)
    {
        var tenant = new TenantContext();
        tenant.Resolve(tenantId);
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_db).Options, tenant, observer);
        _open.Add(db);
        return db;
    }

    public async Task<AccessGraph> ReconcileAndLoadAsync()
    {
        await AccessSeeder.ReconcileTenantAsync(Context(TenantA), TenantA);
        return await AccessGraph.LoadAsync(Context(TenantA));
    }

    public ManagerController ManagerDesk(Guid managerId) =>
        new(Context(TenantA), new Hasher(), new NoSummary(), Options)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim("sub", managerId.ToString()), new Claim("role", "Manager")], "test")),
                },
            },
        };

    public void Dispose()
    {
        foreach (var db in _open)
            db.Dispose();
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
