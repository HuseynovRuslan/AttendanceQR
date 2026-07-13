using System.Security.Claims;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AttendanceQR.Api.Controllers;

/// <summary>Read + export of attendance summaries. Scope is enforced in the service, not here.</summary>
[ApiController]
[Authorize]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly IReportQueryService _reports;
    private readonly IExcelReportExporter _exporter;

    public ReportsController(IReportQueryService reports, IExcelReportExporter exporter)
    {
        _reports = reports;
        _exporter = exporter;
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? locationId)
    {
        if (!TryGetCaller(out var requesterId, out var role))
            return Unauthorized(new { error = "InvalidToken" });

        var (access, report) = await _reports.GetSummaryAsync(
            from, to, locationId, requesterId, role, HttpContext.RequestAborted);

        if (access == ReportAccess.Forbidden)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden" });

        return Ok(report);
    }

    [HttpGet("summary/export")]
    public async Task<IActionResult> Export(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? locationId)
    {
        if (!TryGetCaller(out var requesterId, out var role))
            return Unauthorized(new { error = "InvalidToken" });

        // Same scope path as the JSON summary — export can't sidestep it.
        var (access, report) = await _reports.GetSummaryAsync(
            from, to, locationId, requesterId, role, HttpContext.RequestAborted);

        if (access == ReportAccess.Forbidden)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden" });

        var bytes = _exporter.Build(report!);
        var fileName = $"attendance_{from:yyyy-MM-dd}_{to:yyyy-MM-dd}.xlsx";
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    // Locations the caller may filter by (report filter dropdown). Admin=all, Manager=managed.
    [HttpGet("my-locations")]
    public async Task<IActionResult> MyLocations()
    {
        if (!TryGetCaller(out var requesterId, out var role))
            return Unauthorized(new { error = "InvalidToken" });

        var locations = await _reports.GetVisibleLocationsAsync(requesterId, role, HttpContext.RequestAborted);
        return Ok(locations);
    }

    // Rich dashboard — KPI tiles, trend/weekday charts, top-5 late — over a date range.
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? locationId)
    {
        if (!TryGetCaller(out var requesterId, out var role))
            return Unauthorized(new { error = "InvalidToken" });

        var (access, report) = await _reports.GetDashboardAsync(
            from, to, locationId, requesterId, role, HttpContext.RequestAborted);

        if (access == ReportAccess.Forbidden)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden" });

        return Ok(report);
    }

    // Live "today" board (computed from raw records, not DailySummary). Scoped by role. An optional
    // ?date=yyyy-MM-dd shows a past day's board instead (same shape, so the UI can browse history).
    [HttpGet("today")]
    public async Task<IActionResult> Today([FromQuery] DateOnly? date)
    {
        if (!TryGetCaller(out var requesterId, out var role))
            return Unauthorized(new { error = "InvalidToken" });

        var rows = await _reports.GetTodayAttendanceAsync(requesterId, role, date, HttpContext.RequestAborted);
        return Ok(rows);
    }

    // GET /api/reports/problems?date=yyyy-MM-dd — every rejected scan on that local day: who could
    // not check in/out, and why. Without this the failures only live in AuditLogs, invisible to staff.
    [HttpGet("problems")]
    public async Task<IActionResult> Problems([FromQuery] DateOnly date)
    {
        if (!TryGetCaller(out var requesterId, out var role))
            return Unauthorized(new { error = "InvalidToken" });

        var (access, report) = await _reports.GetProblemsAsync(date, requesterId, role, HttpContext.RequestAborted);

        if (access == ReportAccess.Forbidden)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden" });

        return Ok(report);
    }

    private bool TryGetCaller(out Guid id, out EmployeeRole role)
    {
        role = default;
        return Guid.TryParse(User.FindFirstValue("sub"), out id)
               && Enum.TryParse(User.FindFirstValue("role"), out role);
    }
}
