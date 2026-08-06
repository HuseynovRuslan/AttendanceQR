using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

// Operational health across every company: who actually uses the product, who has gone quiet, and who is
// bumping their plan limits. "Created 3 weeks ago" tells you nothing; the last scan and this month's
// check-ins tell you whether a company is alive. Reads span tenants (IgnoreQueryFilters), operator-only.
public partial class SuperAdminController
{
    // A tenant is HEALTHY if it scanned in the last 2 days, QUIET at 3–14, DORMANT beyond that, and NEW if
    // nobody has ever scanned. The board sorts the ones that need attention to the top.
    private static string HealthStatus(int? daysSinceLastScan) => daysSinceLastScan switch
    {
        null => "new",
        <= 2 => "healthy",
        <= 14 => "quiet",
        _ => "dormant",
    };

    private static int StatusRank(string status) => status switch
    {
        "dormant" => 0,
        "new" => 1,
        "quiet" => 2,
        _ => 3, // healthy
    };

    // GET /api/super/health — every active company with its live usage + activity signals.
    [HttpGet("health")]
    public async Task<IActionResult> Health()
    {
        if (!IsSuperAdmin)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "NotSuperAdmin" });

        var ct = HttpContext.RequestAborted;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        var tenants = await _db.Tenants.Where(t => t.IsActive).ToListAsync(ct);

        var empCounts = await _db.Employees.IgnoreQueryFilters()
            .Where(e => e.IsActive)
            .GroupBy(e => e.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, ct);

        var locCounts = await _db.Locations.IgnoreQueryFilters()
            .GroupBy(l => l.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, ct);

        var lastScan = await _db.AttendanceRecords.IgnoreQueryFilters()
            .GroupBy(r => r.TenantId)
            .Select(g => new { TenantId = g.Key, Last = g.Max(x => x.AttendanceDate) })
            .ToDictionaryAsync(x => x.TenantId, x => x.Last, ct);

        var todayCounts = await _db.AttendanceRecords.IgnoreQueryFilters()
            .Where(r => r.AttendanceDate == today && r.CheckInAtUtc != null)
            .GroupBy(r => r.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, ct);

        var monthCounts = await _db.AttendanceRecords.IgnoreQueryFilters()
            .Where(r => r.AttendanceDate >= monthStart && r.CheckInAtUtc != null)
            .GroupBy(r => r.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, ct);

        var rows = tenants.Select(t =>
        {
            var emp = empCounts.GetValueOrDefault(t.Id, 0);
            int? daysSince = lastScan.TryGetValue(t.Id, out var last) ? today.DayNumber - last.DayNumber : null;
            var status = HealthStatus(daysSince);
            return new
            {
                tenantId = t.Id,
                slug = t.Slug,
                displayName = t.DisplayName,
                plan = t.Plan,
                employeeCount = emp,
                maxEmployees = t.MaxEmployees,
                overEmployeeLimit = t.MaxEmployees is int cap && emp > cap,
                locationCount = locCounts.GetValueOrDefault(t.Id, 0),
                maxLocations = t.MaxLocations,
                lastScanDate = lastScan.TryGetValue(t.Id, out var d) ? d.ToString("yyyy-MM-dd") : null,
                daysSinceLastScan = daysSince,
                checkInsToday = todayCounts.GetValueOrDefault(t.Id, 0),
                checkInsThisMonth = monthCounts.GetValueOrDefault(t.Id, 0),
                status,
            };
        })
        .OrderBy(r => StatusRank(r.status))
        .ThenBy(r => r.displayName)
        .ToList();

        return Ok(new
        {
            summary = new
            {
                healthy = rows.Count(r => r.status == "healthy"),
                quiet = rows.Count(r => r.status == "quiet"),
                dormant = rows.Count(r => r.status is "dormant" or "new"),
                totalActiveUsers = rows.Sum(r => r.employeeCount),
                checkInsToday = rows.Sum(r => r.checkInsToday),
            },
            rows,
        });
    }
}
