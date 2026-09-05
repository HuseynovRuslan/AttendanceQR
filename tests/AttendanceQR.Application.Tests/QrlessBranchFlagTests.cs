using System.Security.Claims;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Security;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Location.QrlessCheckIn is how a whole branch records its day, and two ways of losing it were found
/// before it shipped. An update that simply does not mention the flag — an admin editing a radius from
/// a tab loaded before the field existed — used to switch it off with no log and no screen saying so:
/// the same null-default wipe EmployeeUpdateRequest is known for. And enabling it with the face
/// service off would have made the check-in GPS-only with no verdict and no flag — a poster-less
/// branch whose only anchor silently did not exist.
/// </summary>
public class QrlessBranchFlagTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000cc");

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public Guid Branch { get; } = Guid.NewGuid();
        public Guid AdminId { get; } = Guid.NewGuid();

        public Harness(bool qrless)
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase($"qrless-flag-{Guid.NewGuid()}").Options,
                tenant);
            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "T", Slug = "t", DisplayName = "T", IsActive = true });
            Db.Locations.Add(new Location
            {
                Id = Branch, TenantId = TenantId, Name = "Aeroport yolu", Latitude = 40.4, Longitude = 49.8,
                RadiusMeters = 500, ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
                LateThresholdMinutes = 15, QrVersion = 1, IsActive = true, QrlessCheckIn = qrless,
            });
            Db.Employees.Add(new Employee
            {
                Id = AdminId, TenantId = TenantId, FullName = "Admin", Role = EmployeeRole.Admin,
                LocationId = Branch, IsActive = true, ActivatedAtUtc = DateTime.UtcNow, PasswordHash = "h",
            });
            Db.SaveChanges();
        }

        public AdminLocationsController Controller(IFaceMatchService face) => new(
            Db,
            new QrTokenService(Options.Create(new QrTokenOptions { Secret = "test-secret-qrless-flag", TtlSeconds = 60 })),
            face,
            NullLogger<AdminLocationsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim("sub", AdminId.ToString()),
                        new Claim("role", EmployeeRole.Admin.ToString()),
                    ], "test")),
                },
            },
        };

        public bool Flag() => Db.Locations.AsNoTracking().Single(l => l.Id == Branch).QrlessCheckIn;

        public void Dispose() => Db.Dispose();
    }

    /// <summary>The edit an admin actually makes: hours and radius, nothing about posters.</summary>
    private static LocationRequest EditWithoutTheFlag() =>
        new("Aeroport yolu", 40.4, 49.8, 600, "08:00", "17:00", 10, 126);

    private static string? ErrorOf(IActionResult result)
    {
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        return bad.Value?.GetType().GetProperty("error")?.GetValue(bad.Value) as string;
    }

    [Fact]
    public async Task An_update_that_does_not_mention_the_flag_leaves_it_exactly_as_it_was()
    {
        using var h = new Harness(qrless: true);
        var result = await h.Controller(new OnFaceMatch()).Update(h.Branch, EditWithoutTheFlag());

        Assert.IsNotType<BadRequestObjectResult>(result);
        Assert.True(h.Flag(), "a payload with no opinion on the flag must not switch the branch's check-in method off");
    }

    [Fact]
    public async Task Enabling_QR_less_check_in_is_refused_while_the_face_service_is_off()
    {
        using var h = new Harness(qrless: false);
        var request = EditWithoutTheFlag() with { QrlessCheckIn = true };

        var result = await h.Controller(new OffFaceMatch()).Update(h.Branch, request);

        Assert.Equal("FaceMatchDisabled", ErrorOf(result));
        Assert.False(h.Flag(), "the refusal must leave the branch as it was");
    }

    [Fact]
    public async Task With_the_face_service_on_the_flag_is_set_and_an_explicit_false_switches_it_off()
    {
        using var h = new Harness(qrless: false);
        var on = h.Controller(new OnFaceMatch());

        await on.Update(h.Branch, EditWithoutTheFlag() with { QrlessCheckIn = true });
        Assert.True(h.Flag());

        // Switching OFF needs no face service — and it must be an explicit false, never an omission.
        await h.Controller(new OffFaceMatch()).Update(h.Branch, EditWithoutTheFlag() with { QrlessCheckIn = false });
        Assert.False(h.Flag());
    }

    [Fact]
    public async Task Creating_a_branch_without_the_flag_creates_a_poster_branch()
    {
        using var h = new Harness(qrless: false);
        var result = await h.Controller(new OffFaceMatch()).Create(EditWithoutTheFlag() with { Name = "Yeni filial" });

        Assert.IsNotType<BadRequestObjectResult>(result);
        Assert.False(h.Db.Locations.AsNoTracking().Single(l => l.Name == "Yeni filial").QrlessCheckIn);
    }
}
