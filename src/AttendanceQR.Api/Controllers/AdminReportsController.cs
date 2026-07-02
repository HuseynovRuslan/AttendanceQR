using AttendanceQR.Application.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AttendanceQR.Api.Controllers;

/// <summary>Manual trigger for summary generation — used for testing and re-computation.</summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/reports")]
public class AdminReportsController : ControllerBase
{
    private readonly IDailySummaryService _summaries;

    public AdminReportsController(IDailySummaryService summaries) => _summaries = summaries;

    [HttpPost("generate")]
    public async Task<IActionResult> Generate([FromQuery] DateOnly date)
    {
        var count = await _summaries.GenerateForDateAsync(date, HttpContext.RequestAborted);
        return Ok(new { date, generated = count });
    }
}
