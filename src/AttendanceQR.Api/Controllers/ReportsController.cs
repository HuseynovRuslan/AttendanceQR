using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Multitenancy;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace AttendanceQR.Api.Controllers;

/// <summary>Read + export of attendance summaries. Scope is enforced in the service, not here.</summary>
[ApiController]
[Authorize]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly IReportQueryService _reports;
    private readonly IExcelReportExporter _exporter;
    private readonly IMemoryCache _cache;

    public ReportsController(IReportQueryService reports, IExcelReportExporter exporter, IMemoryCache cache)
    {
        _reports = reports;
        _exporter = exporter;
        _cache = cache;
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

    /// <summary>
    /// The CALLER's own figures — the three tiles and the absence banner on their profile screen.
    ///
    /// That screen used to call <c>summary</c>, which scopes by ROLE: an employee gets themselves, a
    /// manager gets their branches, an admin gets the company. So on their own profile card an admin
    /// was shown the whole company's totals as if they were personal — 947 days worked, 7,893 hours,
    /// and a banner reading "601 days absent this month", which is not a number a person can have.
    ///
    /// It is not only nonsense, it is the wrong shape of nonsense: the banner tells the reader to go
    /// and ask their manager to correct an absence that was never theirs.
    ///
    /// Passing Employee explicitly is the whole fix. The scope switch already has the right branch —
    /// "Employee: only themselves, whatever locationId was passed" — this endpoint just refuses to let
    /// the caller's role choose a wider one.
    /// </summary>
    [HttpGet("my-summary")]
    public async Task<IActionResult> MySummary([FromQuery] DateOnly from, [FromQuery] DateOnly to)
    {
        var (access, report) = await _reports.GetSummaryAsync(
            from, to, null, User.EmployeeId(), EmployeeRole.Employee, HttpContext.RequestAborted);

        if (access == ReportAccess.Forbidden)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden" });

        return Ok(report);
    }

    // One employee's day-by-day breakdown — the profile summary tiles expand into these days.
    [HttpGet("employee-days")]
    public async Task<IActionResult> EmployeeDays(
        [FromQuery] Guid employeeId, [FromQuery] DateOnly from, [FromQuery] DateOnly to)
    {
        var (access, days) = await _reports.GetEmployeeDaysAsync(
            employeeId, from, to, User.EmployeeId(), User.Role(), HttpContext.RequestAborted);
        if (access == ReportAccess.Forbidden)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden" });
        return Ok(days);
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
    //
    // The LIVE board is cached 10s PER REQUESTER: the dashboard, the today board and the wall kiosk
    // all poll this endpoint every 20-30s — usually under the same admin account — and each poll
    // recomputes the whole roster. The key includes the requester precisely because the service
    // scopes the rows by role (a manager sees only their branches); a tenant-wide key would serve
    // one person's board to another. Ten seconds is invisible at those poll rates, and a historical
    // ?date= browse is rare enough to stay uncached.
    [HttpGet("today")]
    public async Task<IActionResult> Today([FromQuery] DateOnly? date)
    {
        var requesterId = User.EmployeeId();
        var role = User.Role();

        if (date is not null)
            return Ok(await _reports.GetTodayAttendanceAsync(requesterId, role, date, HttpContext.RequestAborted));

        var rows = await _cache.GetOrCreateAsync($"today-board:{requesterId}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10);
            return _reports.GetTodayAttendanceAsync(requesterId, role, null, HttpContext.RequestAborted);
        });
        return Ok(rows);
    }

    // POST /api/reports/export-day — the workbook the leadership receives every morning.
    //
    // It used to be one flat A-to-Z list of the whole company, which the reader then had to sort by
    // site before it answered anything. It is now two sheets — «Xülasə», a line per site with the
    // day's counts, and «Davamiyyət», the same people grouped under collapsible site banners. The
    // shaping lives in DayBoardSheet, which is pure and therefore has tests.
    //
    // The rows still arrive FROM the client. That is not laziness: the status label and the bucket
    // both need the leave type, and re-deriving them here would put the «Ezamiyyət exported as
    // Məzuniyyət» bug in a second place. What the board shows is what the file says.
    [HttpPost("export-day")]
    public IActionResult ExportDay([FromBody] ExportDayRequest request)
    {
        var data = request.Rows ?? [];
        if (data.Count > 5000)
            return BadRequest(new { error = "TooManyRows" });

        var rows = data
            .Select(row => new DayBoardSheet.Row(
                row.Name ?? string.Empty,
                row.Position ?? string.Empty,
                row.Location ?? string.Empty,
                row.Status ?? string.Empty,
                row.CheckIn ?? string.Empty,
                row.CheckOut ?? string.Empty,
                row.Photo ?? string.Empty,
                row.Bucket))
            .ToList();

        var title = string.IsNullOrWhiteSpace(request.Title) ? "Davamiyyət" : request.Title;
        var bytes = DayBoardSheet.Build(title, rows, request.ScopeNote, request.BucketLabels);

        var safeDate = string.IsNullOrWhiteSpace(request.Date) ? "gun" : request.Date;
        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"davamiyyet-{safeDate}.xlsx");
    }

    // GET /api/reports/payroll?from=&to=&locationId= — the payroll (Maaş) table: each employee's
    // fixed monthly salary minus a per-day share for unexcused absences. Same scope as the summary.
    // Manager too: a branch manager who cannot see what their own staff are owed has to ask the admin
    // about every question of it. The role ceiling is applied INSIDE GetPayrollAsync, not by the shared
    // scope — that helper deliberately carries every role, because a board headcount that quietly
    // omitted the managers read short. This comment used to claim the scope filtered by role. It did
    // not, and for as long as it said so a manager's payroll table listed their peers' and their
    // admin's salary.
    [HttpGet("payroll")]
    [Authorize(Roles = "Admin,Manager")]
    [RequireFeature(TenantFeatures.Payroll)]
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
    [RequireFeature(TenantFeatures.Payroll)]
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

    // GET /api/reports/tabel?year=&month=&locationId= — the monthly timesheet grid (Aylıq Tabel).
    // Manager-visible (scoped in the service), because a manager reconciles their own branch's month.
    [HttpGet("tabel")]
    public async Task<IActionResult> Tabel(
        [FromQuery] int year, [FromQuery] int month, [FromQuery] Guid? locationId)
    {
        var (access, report) = await _reports.GetTabelAsync(
            year, month, locationId, User.EmployeeId(), User.Role(), HttpContext.RequestAborted);

        if (access == ReportAccess.Forbidden)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden" });

        return Ok(report);
    }

    // GET /api/reports/tabel/export — the same grid as a printable/archivable .xlsx.
    [HttpGet("tabel/export")]
    public async Task<IActionResult> TabelExport(
        [FromQuery] int year, [FromQuery] int month, [FromQuery] Guid? locationId)
    {
        var (access, report) = await _reports.GetTabelAsync(
            year, month, locationId, User.EmployeeId(), User.Role(), HttpContext.RequestAborted);

        if (access == ReportAccess.Forbidden)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden" });

        var bytes = _exporter.BuildTabel(report!);
        var fileName = $"tabel_{year}_{month:D2}.xlsx";
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    // GET /api/reports/problems?from=yyyy-MM-dd&to=yyyy-MM-dd — every rejected scan across the range:
    // who could not check in/out, and why. A range, not a day, so a problem from earlier in the week
    // does not silently age out of view. `date` is still accepted as a single-day shorthand.
    [HttpGet("problems")]
    public async Task<IActionResult> Problems([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] DateOnly? date)
    {
        var requesterId = User.EmployeeId();
        var role = User.Role();

        var toDay = to ?? date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var fromDay = from ?? date ?? toDay.AddDays(-6); // default: the last 7 days

        var (access, report) = await _reports.GetProblemsAsync(fromDay, toDay, requesterId, role, HttpContext.RequestAborted);

        if (access == ReportAccess.Forbidden)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden" });

        return Ok(report);
    }


    // GET /api/reports/shift-mismatch?days=21 — people whose real arrival times disagree with the
    // shift they are on. Read-only and accusatory of nothing: a mismatch is a question about the
    // SCHEDULE, not about the employee. See ShiftFit for why it exists.
    [HttpGet("shift-mismatch")]
    public async Task<IActionResult> ShiftMismatch([FromQuery] int days = 21)
    {
        var (access, report) = await _reports.GetShiftMismatchAsync(
            days, User.EmployeeId(), User.Role(), HttpContext.RequestAborted);

        if (access == ReportAccess.Forbidden)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden" });

        return Ok(report);
    }

    // GET /api/reports/stuck-devices — people whose phones refuse to scan (GPS/camera family) with no
    // success since. Feeds the register at the top of the Problems screen; the fix is a phone-settings
    // walk, so each row carries the phone number and platform for the supervisor.
    [HttpGet("stuck-devices")]
    public async Task<IActionResult> StuckDevices()
    {
        var (access, rows) = await _reports.GetStuckDevicesAsync(
            User.EmployeeId(), User.Role(), HttpContext.RequestAborted);
        if (access == ReportAccess.Forbidden)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden" });
        return Ok(rows);
    }

}
