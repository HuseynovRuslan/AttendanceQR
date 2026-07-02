using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// Development-only helpers for manually exercising the flow. Every action is gated behind
/// <see cref="IWebHostEnvironment.IsDevelopment"/> and 404s outside Development.
/// </summary>
[ApiController]
[Route("api/dev")]
public class DevController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IQrTokenService _qrTokenService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IWebHostEnvironment _environment;

    public DevController(
        AppDbContext db,
        IQrTokenService qrTokenService,
        IPasswordHasher passwordHasher,
        IWebHostEnvironment environment)
    {
        _db = db;
        _qrTokenService = qrTokenService;
        _passwordHasher = passwordHasher;
        _environment = environment;
    }

    // GET /api/dev/qr/{locationId} → a freshly signed token for manual QR testing.
    [HttpGet("qr/{locationId:guid}")]
    public IActionResult GenerateQr(Guid locationId)
    {
        if (!_environment.IsDevelopment())
            return NotFound();

        var token = _qrTokenService.Generate(locationId);
        return Ok(new { locationId, token });
    }

    // POST /api/dev/seed → two Locations and four fully-activated employees (1 Admin, 1 Manager,
    // 2 Employees across the two locations) so role and resource-level checks can be exercised
    // straight away. All share the password below and come with a bound device. Idempotent: if the
    // fixtures already exist it just returns them.
    private const string SeedPassword = "Passw0rd!";

    [HttpPost("seed")]
    public async Task<IActionResult> Seed()
    {
        if (!_environment.IsDevelopment())
            return NotFound();

        if (!await _db.Employees.AnyAsync(e => e.Email == "admin@test.com"))
        {
            var loc1 = new Location
            {
                Name = "Baku HQ",
                Latitude = 40.4093,
                Longitude = 49.8671,
                RadiusMeters = 150,
                ShiftStart = new TimeOnly(9, 0),
                ShiftEnd = new TimeOnly(18, 0),
                LateThresholdMinutes = 15
            };
            var loc2 = new Location
            {
                Name = "Ganja Office",
                Latitude = 40.6828,
                Longitude = 46.3606,
                RadiusMeters = 150,
                ShiftStart = new TimeOnly(9, 0),
                ShiftEnd = new TimeOnly(18, 0),
                LateThresholdMinutes = 15
            };
            _db.Locations.AddRange(loc1, loc2);

            var passwordHash = _passwordHasher.Hash(SeedPassword);
            var now = DateTime.UtcNow;

            Employee Make(string name, string email, EmployeeRole role, Guid locationId) => new()
            {
                FullName = name,
                Email = email,
                Role = role,
                LocationId = locationId,
                PasswordHash = passwordHash,
                IsActive = true,
                ActivatedAtUtc = now
            };

            var admin = Make("Admin User", "admin@test.com", EmployeeRole.Admin, loc1.Id);
            var manager = Make("Manager User", "manager@test.com", EmployeeRole.Manager, loc1.Id);
            var emp1 = Make("Employee One", "emp1@test.com", EmployeeRole.Employee, loc1.Id);
            var emp2 = Make("Employee Two", "emp2@test.com", EmployeeRole.Employee, loc2.Id);
            _db.Employees.AddRange(admin, manager, emp1, emp2);

            DeviceBinding Bind(Guid employeeId, string fingerprint) => new()
            {
                EmployeeId = employeeId,
                DeviceFingerprint = fingerprint,
                BoundAtUtc = now,
                IsActive = true
            };
            _db.DeviceBindings.AddRange(
                Bind(admin.Id, "admin-dev"),
                Bind(manager.Id, "mgr-dev"),
                Bind(emp1.Id, "emp1-dev"),
                Bind(emp2.Id, "emp2-dev"));

            // The manager oversees loc1 only (NOT loc2) — lets us prove report scope: 200 for
            // loc1, 403 for loc2.
            _db.ManagedLocations.Add(new ManagedLocation { EmployeeId = manager.Id, LocationId = loc1.Id });

            await _db.SaveChangesAsync();
        }

        // Build the response from the DB so the shape is identical whether we just seeded or not.
        var emails = new[] { "admin@test.com", "manager@test.com", "emp1@test.com", "emp2@test.com" };
        var employees = await _db.Employees
            .Where(e => emails.Contains(e.Email))
            .ToListAsync();
        var byEmail = employees.ToDictionary(e => e.Email);
        var employeeIds = employees.Select(e => e.Id).ToList();
        var devices = await _db.DeviceBindings
            .Where(d => employeeIds.Contains(d.EmployeeId))
            .ToDictionaryAsync(d => d.EmployeeId, d => d.DeviceFingerprint);
        var locationIds = employees.Select(e => e.LocationId).Distinct().ToList();
        var locations = await _db.Locations
            .Where(l => locationIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id);

        object Dto(string email)
        {
            var e = byEmail[email];
            return new
            {
                role = e.Role.ToString(),
                email = e.Email,
                id = e.Id,
                device = devices.GetValueOrDefault(e.Id),
                locationId = e.LocationId
            };
        }

        var location1 = locations[byEmail["emp1@test.com"].LocationId];
        var location2 = locations[byEmail["emp2@test.com"].LocationId];

        return Ok(new
        {
            password = SeedPassword,
            location1 = new { id = location1.Id, location1.Latitude, location1.Longitude, location1.RadiusMeters },
            location2 = new { id = location2.Id, location2.Latitude, location2.Longitude, location2.RadiusMeters },
            employees = new[]
            {
                Dto("admin@test.com"),
                Dto("manager@test.com"),
                Dto("emp1@test.com"),
                Dto("emp2@test.com")
            }
        });
    }

    // POST /api/dev/seed-attendance → raw AttendanceRecords across two past days covering every
    // scenario (on-time+overtime, late, incomplete, absent) so DailySummary computation can be
    // verified. Comments show Baku-local times; stored as UTC (local − 4h). Idempotent.
    [HttpPost("seed-attendance")]
    public async Task<IActionResult> SeedAttendance()
    {
        if (!_environment.IsDevelopment())
            return NotFound();

        var emp1 = await _db.Employees.FirstOrDefaultAsync(e => e.Email == "emp1@test.com");
        var emp2 = await _db.Employees.FirstOrDefaultAsync(e => e.Email == "emp2@test.com");
        if (emp1 is null || emp2 is null)
            return BadRequest(new { error = "Run /api/dev/seed first" });

        var dateA = new DateOnly(2026, 6, 30);
        var dateB = new DateOnly(2026, 6, 29);

        async Task AddIfMissing(Employee e, DateOnly date, DateTime inUtc, DateTime? outUtc, AttendanceStatus status)
        {
            if (await _db.AttendanceRecords.AnyAsync(r => r.EmployeeId == e.Id && r.AttendanceDate == date))
                return;
            _db.AttendanceRecords.Add(new AttendanceRecord
            {
                EmployeeId = e.Id,
                LocationId = e.LocationId,
                AttendanceDate = date,
                CheckInAtUtc = inUtc,
                CheckOutAtUtc = outUtc,
                Status = status
            });
        }

        // emp1 @ 06-30: in 09:05, out 18:30 local → OnTime, worked 565, overtime 30.
        await AddIfMissing(emp1, dateA,
            new DateTime(2026, 6, 30, 5, 5, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 30, 14, 30, 0, DateTimeKind.Utc), AttendanceStatus.OnTime);
        // emp1 @ 06-29: in 09:45, out 17:00 local → Late, LateMinutes 45, worked 435.
        await AddIfMissing(emp1, dateB,
            new DateTime(2026, 6, 29, 5, 45, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 29, 13, 0, 0, DateTimeKind.Utc), AttendanceStatus.Late);
        // emp2 @ 06-30: in 09:10 local, NO check-out → Incomplete.
        await AddIfMissing(emp2, dateA,
            new DateTime(2026, 6, 30, 5, 10, 0, DateTimeKind.Utc), null, AttendanceStatus.OnTime);
        // emp2 @ 06-29: no record at all → Absent.

        await _db.SaveChangesAsync();

        return Ok(new
        {
            dates = new[] { dateB.ToString("yyyy-MM-dd"), dateA.ToString("yyyy-MM-dd") },
            scenarios = new[]
            {
                new { email = "emp1@test.com", date = "2026-06-30", expect = "OnTime, worked 565, overtime 30" },
                new { email = "emp1@test.com", date = "2026-06-29", expect = "Late, LateMinutes 45, worked 435" },
                new { email = "emp2@test.com", date = "2026-06-30", expect = "Incomplete, worked 0" },
                new { email = "emp2@test.com", date = "2026-06-29", expect = "Absent (no record)" }
            }
        });
    }
}
