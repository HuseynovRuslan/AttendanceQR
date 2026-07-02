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

    private bool TryGetCaller(out Guid id, out EmployeeRole role)
    {
        role = default;
        return Guid.TryParse(User.FindFirstValue("sub"), out id)
               && Enum.TryParse(User.FindFirstValue("role"), out role);
    }
}
