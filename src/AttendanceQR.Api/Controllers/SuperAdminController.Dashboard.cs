using AttendanceQR.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

// Cross-tenant READ models for the platform back-office: the at-a-glance dashboard and one tenant's
// detail. Everything here is IgnoreQueryFilters on purpose — this is the one place meant to see across
// companies — and read-only, so nothing is audited.
public partial class SuperAdminController
{
    // GET /api/super/dashboard — the numbers that describe the whole platform at a glance, plus a short
    // "needs attention" list (disabled, head-less, or gone-silent companies).
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        if (!IsSuperAdmin)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "NotSuperAdmin" });

        var ct = HttpContext.RequestAborted;
        var tz = TimeZoneInfo.FindSystemTimeZoneById(_appOptions.TimeZone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var staleBefore = today.AddDays(-7);

        var tenants = await _db.Tenants.ToListAsync(ct);

        var employeeCounts = await _db.Employees.IgnoreQueryFilters()
            .Where(e => e.IsActive)
            .GroupBy(e => e.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, ct);

        var adminCounts = await _db.Employees.IgnoreQueryFilters()
            .Where(e => e.IsActive && e.Role == EmployeeRole.Admin)
            .GroupBy(e => e.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, ct);

        var lastScan = await _db.AttendanceRecords.IgnoreQueryFilters()
            .GroupBy(r => r.TenantId)
            .Select(g => new { TenantId = g.Key, Last = g.Max(x => x.AttendanceDate) })
            .ToDictionaryAsync(x => x.TenantId, x => x.Last, ct);

        var checkInsToday = await _db.AttendanceRecords.IgnoreQueryFilters()
            .CountAsync(r => r.AttendanceDate == today && r.CheckInAtUtc != null, ct);
        var checkInsThisMonth = await _db.AttendanceRecords.IgnoreQueryFilters()
            .CountAsync(r => r.AttendanceDate >= monthStart && r.CheckInAtUtc != null, ct);

        // A company needs attention when it is disabled, headless (no active admin — nobody can manage
        // it), or has gone silent (active but no scan in a week / ever). This is the operator's to-do.
        var attention = new List<object>();
        foreach (var t in tenants)
        {
            var admins = adminCounts.GetValueOrDefault(t.Id, 0);
            var hasScan = lastScan.TryGetValue(t.Id, out var last);
            string? reason = null;
            if (!t.IsActive) reason = "Deaktiv";
            else if (admins == 0) reason = "Adminsiz";
            else if (!hasScan) reason = "Heç vaxt skan olmayıb";
            else if (last < staleBefore) reason = $"Susqun ({last:yyyy-MM-dd})";
            if (reason is not null)
                attention.Add(new { id = t.Id, slug = t.Slug, displayName = t.DisplayName, reason });
        }

        return Ok(new
        {
            totalTenants = tenants.Count,
            activeTenants = tenants.Count(t => t.IsActive),
            totalEmployees = employeeCounts.Values.Sum(),
            checkInsToday,
            checkInsThisMonth,
            attention,
        });
    }

    // GET /api/super/tenants/{id} — one company in depth: counts, its active admins, and the recent
    // platform actions taken on it (pulled from the audit trail).
    [HttpGet("tenants/{id:guid}")]
    public async Task<IActionResult> TenantDetail(Guid id)
    {
        if (!IsSuperAdmin)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "NotSuperAdmin" });

        var ct = HttpContext.RequestAborted;
        var t = await _db.Tenants.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null)
            return NotFound(new { error = "TenantNotFound" });

        var tz = TimeZoneInfo.FindSystemTimeZoneById(_appOptions.TimeZone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        var employeeCount = await _db.Employees.IgnoreQueryFilters().CountAsync(e => e.TenantId == id && e.IsActive, ct);
        var locationCount = await _db.Locations.IgnoreQueryFilters().CountAsync(l => l.TenantId == id, ct);
        var checkInsThisMonth = await _db.AttendanceRecords.IgnoreQueryFilters()
            .CountAsync(r => r.TenantId == id && r.AttendanceDate >= monthStart && r.CheckInAtUtc != null, ct);
        var lastScan = await _db.AttendanceRecords.IgnoreQueryFilters()
            .Where(r => r.TenantId == id)
            .MaxAsync(r => (DateOnly?)r.AttendanceDate, ct);

        var admins = await _db.Employees.IgnoreQueryFilters()
            .Where(e => e.TenantId == id && e.Role == EmployeeRole.Admin && e.IsActive)
            .OrderBy(e => e.FullName)
            .Select(e => new { id = e.Id, name = e.FullName, phone = e.PhoneNumber })
            .ToListAsync(ct);

        var recentActions = await _db.SuperAdminAuditLogs
            .Where(a => a.TargetTenantId == id)
            .OrderByDescending(a => a.CreatedAtUtc)
            .Take(20)
            .Select(a => new { actorName = a.ActorName, action = a.Action, details = a.Details, createdAtUtc = a.CreatedAtUtc })
            .ToListAsync(ct);

        return Ok(new
        {
            id = t.Id,
            slug = t.Slug,
            displayName = t.DisplayName,
            name = t.Name,
            color = t.Color,
            logoUrl = t.LogoKey,
            host = $"{t.Slug}.qrlog.az",
            isActive = t.IsActive,
            createdAtUtc = t.CreatedAtUtc,
            employeeCount,
            locationCount,
            checkInsThisMonth,
            lastScanDate = lastScan?.ToString("yyyy-MM-dd"),
            admins,
            recentActions,
        });
    }
}
