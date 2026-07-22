using AttendanceQR.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// The group console: every company at once, on one screen, live.
///
/// The tenant panels answer "how is my company doing today". This answers a different question —
/// "how much is running on this system right now" — and it is the only view where the three companies
/// appear side by side. Every query here deliberately ignores the tenant filter, which is safe for
/// exactly one reason: the endpoint is gated on the super-admin allowlist, not on a role that a
/// company's own Admin could grant themselves.
/// </summary>
public partial class SuperAdminController
{
    /// <summary>Days of history behind the trend line — two working weeks reads as a trend without
    /// crushing the chart on a phone.</summary>
    private const int TrendDays = 14;

    /// <summary>Scans in the live feed. Enough that something moves while you watch, few enough to
    /// stay readable across the room.</summary>
    private const int FeedSize = 14;

    // GET /api/super/hq — everything the group board shows, in one round trip. One call rather than
    // six because the board refreshes on a timer: six requests on a loop is six chances for the
    // screen to show half-old numbers mid-demo.
    [HttpGet("hq")]
    public async Task<IActionResult> GroupOverview()
    {
        if (!IsSuperAdmin)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "NotSuperAdmin" });

        var ct = HttpContext.RequestAborted;
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(_appOptions.TimeZone);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);
        var today = DateOnly.FromDateTime(nowLocal);
        var trendFrom = today.AddDays(-(TrendDays - 1));

        var tenants = await _db.Tenants
            .Where(t => t.IsActive)
            .OrderBy(t => t.CreatedAtUtc)
            .Select(t => new { t.Id, t.Slug, t.DisplayName, t.Name })
            .ToListAsync(ct);

        // IgnoreQueryFilters throughout: this screen exists to look across companies.
        var employees = await _db.Employees.IgnoreQueryFilters()
            .Where(e => e.IsActive)
            .Select(e => new { e.Id, e.TenantId, e.FullName, e.MonthlySalary, e.LocationId })
            .ToListAsync(ct);

        var allLocations = await _db.Locations.IgnoreQueryFilters()
            .Select(l => new { l.Id, l.TenantId, l.Name, l.Latitude, l.Longitude })
            .ToListAsync(ct);
        var locationCount = allLocations
            .GroupBy(l => l.TenantId)
            .ToDictionary(g => g.Key, g => g.Count());

        var todayRecords = await _db.AttendanceRecords.IgnoreQueryFilters()
            .Where(r => r.AttendanceDate == today && r.CheckInAtUtc != null)
            .Select(r => new { r.TenantId, r.EmployeeId, r.LocationId, r.CheckOutAtUtc })
            .ToListAsync(ct);

        var trendRows = await _db.AttendanceRecords.IgnoreQueryFilters()
            .Where(r => r.AttendanceDate >= trendFrom && r.AttendanceDate <= today && r.CheckInAtUtc != null)
            .Select(r => new { r.TenantId, r.AttendanceDate, r.EmployeeId })
            .ToListAsync(ct);

        var byTenant = employees.GroupBy(e => e.TenantId).ToDictionary(g => g.Key, g => g.ToList());
        var presentByTenant = todayRecords.GroupBy(r => r.TenantId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.EmployeeId).Distinct().Count());
        var onDutyByTenant = todayRecords.Where(r => r.CheckOutAtUtc == null)
            .GroupBy(r => r.TenantId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.EmployeeId).Distinct().Count());

        var companies = tenants.Select(t =>
        {
            var staff = byTenant.GetValueOrDefault(t.Id, new());
            var present = presentByTenant.GetValueOrDefault(t.Id, 0);
            return new
            {
                id = t.Id,
                slug = t.Slug,
                name = string.IsNullOrWhiteSpace(t.DisplayName) ? t.Name : t.DisplayName,
                employees = staff.Count,
                present,
                onDuty = onDutyByTenant.GetValueOrDefault(t.Id, 0),
                locations = locationCount.GetValueOrDefault(t.Id, 0),
                // Share of the workforce that has turned up today. Deliberately plain: a director
                // reads "94% came in" without being told what a scheduled work day is.
                attendancePct = staff.Count == 0 ? 0 : (int)Math.Round(present * 100.0 / staff.Count),
                payroll = staff.Sum(e => e.MonthlySalary ?? 0m),
            };
        }).ToList();

        // The trend is the group total per day — three overlapping lines on a projector is noise.
        var trend = Enumerable.Range(0, TrendDays)
            .Select(offset =>
            {
                var date = trendFrom.AddDays(offset);
                return new
                {
                    date,
                    present = trendRows.Where(r => r.AttendanceDate == date)
                        .Select(r => r.EmployeeId).Distinct().Count(),
                };
            })
            .ToList();

        // Where the work is happening right now. The point of putting this on a map is that a
        // director recognises their own sites instantly — a table of the same numbers does not carry
        // the same thing at all.
        var onDutyByLocation = todayRecords.Where(r => r.CheckOutAtUtc == null)
            .GroupBy(r => r.LocationId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.EmployeeId).Distinct().Count());
        var presentByLocation = todayRecords
            .GroupBy(r => r.LocationId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.EmployeeId).Distinct().Count());
        var tenantOrder = tenants.Select(t => t.Id).ToList();

        var sites = allLocations
            // A site with no coordinates cannot be drawn, and a marker at (0,0) lands in the Atlantic.
            .Where(l => l.Latitude != 0 || l.Longitude != 0)
            .Select(l => new
            {
                id = l.Id,
                name = l.Name,
                companyIndex = tenantOrder.IndexOf(l.TenantId),
                lat = l.Latitude,
                lng = l.Longitude,
                onDuty = onDutyByLocation.GetValueOrDefault(l.Id, 0),
                present = presentByLocation.GetValueOrDefault(l.Id, 0),
                staff = employees.Count(e => e.LocationId == l.Id),
            })
            .OrderByDescending(s => s.onDuty)
            .ToList();

        var names = employees.ToDictionary(e => e.Id, e => e.FullName);
        var tenantNames = tenants.ToDictionary(
            t => t.Id, t => string.IsNullOrWhiteSpace(t.DisplayName) ? t.Name : t.DisplayName);
        var locationNames = await _db.Locations.IgnoreQueryFilters()
            .Select(l => new { l.Id, l.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        // The feed is what makes the board look alive: rows arrive while someone is watching it.
        // Check-outs count as events too — a board that only ever shows arrivals goes still by noon.
        var feedFrom = today.AddDays(-1);
        var feed = (await _db.AttendanceRecords.IgnoreQueryFilters()
                .Where(r => r.AttendanceDate >= feedFrom && r.CheckInAtUtc != null)
                .Select(r => new { r.TenantId, r.EmployeeId, r.LocationId, r.CheckInAtUtc, r.CheckOutAtUtc })
                .ToListAsync(ct))
            .SelectMany(r => new[]
            {
                new { r.TenantId, r.EmployeeId, r.LocationId, At = r.CheckInAtUtc!.Value, Kind = "in" },
                r.CheckOutAtUtc is null
                    ? null
                    : new { r.TenantId, r.EmployeeId, r.LocationId, At = r.CheckOutAtUtc.Value, Kind = "out" },
            })
            .Where(x => x is not null)
            .Select(x => x!)
            .OrderByDescending(x => x.At)
            .Take(FeedSize)
            .Select(x => new
            {
                fullName = names.GetValueOrDefault(x.EmployeeId, "—"),
                company = tenantNames.GetValueOrDefault(x.TenantId, ""),
                location = locationNames.GetValueOrDefault(x.LocationId, ""),
                atUtc = x.At,
                kind = x.Kind,
            })
            .ToList();

        return Ok(new
        {
            generatedAtUtc = DateTime.UtcNow,
            totals = new
            {
                companies = companies.Count,
                employees = companies.Sum(c => c.employees),
                present = companies.Sum(c => c.present),
                onDuty = companies.Sum(c => c.onDuty),
                locations = companies.Sum(c => c.locations),
                payroll = companies.Sum(c => c.payroll),
                attendancePct = companies.Sum(c => c.employees) == 0
                    ? 0
                    : (int)Math.Round(companies.Sum(c => c.present) * 100.0 / companies.Sum(c => c.employees)),
                // Scans handled since the system went live — the number that says "this is in real
                // use", which is the whole point of showing the board to someone who is being sold to.
                totalScans = await _db.AttendanceRecords.IgnoreQueryFilters()
                    .CountAsync(r => r.CheckInAtUtc != null, ct),
            },
            companies,
            sites,
            trend,
            feed,
        });
    }
}
