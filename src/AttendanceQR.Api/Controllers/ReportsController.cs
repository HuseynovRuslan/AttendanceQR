using AttendanceQR.Api.Contracts;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Enums;
using ClosedXML.Excel;
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
        var requesterId = User.EmployeeId();
        var role = User.Role();

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
        var requesterId = User.EmployeeId();
        var role = User.Role();

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
        var requesterId = User.EmployeeId();
        var role = User.Role();

        var locations = await _reports.GetVisibleLocationsAsync(requesterId, role, HttpContext.RequestAborted);
        return Ok(locations);
    }

    // Rich dashboard — KPI tiles, trend/weekday charts, top-5 late — over a date range.
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? locationId)
    {
        var requesterId = User.EmployeeId();
        var role = User.Role();

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
        var requesterId = User.EmployeeId();
        var role = User.Role();

        var rows = await _reports.GetTodayAttendanceAsync(requesterId, role, date, HttpContext.RequestAborted);
        return Ok(rows);
    }

    // POST /api/reports/export-day — format the (already filtered) board the admin sees into a tidy
    // .xlsx: a title line, a coloured header row, borders and sensible column widths. The client sends
    // exactly what's on screen, so any active filters carry through.
    [HttpPost("export-day")]
    public IActionResult ExportDay([FromBody] ExportDayRequest request)
    {
        var data = request.Rows ?? [];
        if (data.Count > 5000)
            return BadRequest(new { error = "TooManyRows" });

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Davamiyyət");

        // Title line across the table.
        ws.Cell(1, 1).Value = string.IsNullOrWhiteSpace(request.Title) ? "Davamiyyət" : request.Title;
        ws.Range(1, 1, 1, 6).Merge();
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;

        var headers = new[] { "Ad Soyad", "Ərazi", "Status", "Giriş", "Çıxış", "Şəkil" };
        for (var i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(2, i + 1);
            c.Value = headers[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#1E70C8");
            c.Style.Font.FontColor = XLColor.White;
            c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        var r = 3;
        foreach (var row in data)
        {
            ws.Cell(r, 1).Value = row.Name ?? string.Empty;
            ws.Cell(r, 2).Value = row.Location ?? string.Empty;
            ws.Cell(r, 3).Value = row.Status ?? string.Empty;
            ws.Cell(r, 4).Value = row.CheckIn ?? string.Empty;
            ws.Cell(r, 5).Value = row.CheckOut ?? string.Empty;
            ws.Cell(r, 6).Value = row.Photo ?? string.Empty;
            r++;
        }

        if (r > 3)
        {
            var table = ws.Range(2, 1, r - 1, 6);
            table.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            table.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        }

        ws.Column(1).Width = 28;
        ws.Column(2).Width = 20;
        ws.Column(3).Width = 16;
        ws.Column(4).Width = 10;
        ws.Column(5).Width = 10;
        ws.Column(6).Width = 9;
        ws.SheetView.FreezeRows(2);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        var safeDate = string.IsNullOrWhiteSpace(request.Date) ? "gun" : request.Date;
        return File(
            ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"davamiyyet-{safeDate}.xlsx");
    }

    // GET /api/reports/payroll?from=&to=&locationId= — the payroll (Maaş) table: each employee's
    // fixed monthly salary minus a per-day share for unexcused absences. Same scope as the summary.
    [HttpGet("payroll")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Payroll(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? locationId)
    {
        var (access, report) = await _reports.GetPayrollAsync(
            from, to, locationId, User.EmployeeId(), User.Role(), HttpContext.RequestAborted);

        if (access == ReportAccess.Forbidden)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden" });

        return Ok(report);
    }

    // GET /api/reports/payroll/export — the same payroll table as a formatted .xlsx for the accountant.
    [HttpGet("payroll/export")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> PayrollExport(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? locationId)
    {
        var (access, report) = await _reports.GetPayrollAsync(
            from, to, locationId, User.EmployeeId(), User.Role(), HttpContext.RequestAborted);

        if (access == ReportAccess.Forbidden)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden" });

        var bytes = _exporter.BuildPayroll(report!);
        var fileName = $"maas_{from:yyyy-MM-dd}_{to:yyyy-MM-dd}.xlsx";
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    // GET /api/reports/problems?date=yyyy-MM-dd — every rejected scan on that local day: who could
    // not check in/out, and why. Without this the failures only live in AuditLogs, invisible to staff.
    [HttpGet("problems")]
    public async Task<IActionResult> Problems([FromQuery] DateOnly date)
    {
        var requesterId = User.EmployeeId();
        var role = User.Role();

        var (access, report) = await _reports.GetProblemsAsync(date, requesterId, role, HttpContext.RequestAborted);

        if (access == ReportAccess.Forbidden)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden" });

        return Ok(report);
    }

}
