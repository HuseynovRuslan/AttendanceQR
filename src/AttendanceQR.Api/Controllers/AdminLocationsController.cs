using System.Security.Claims;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AttendanceQR.Api.Controllers;

/// <summary>All locations, for the admin invite dropdown. Admin-only.</summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/locations")]
public class AdminLocationsController : ControllerBase
{
    private readonly IReportQueryService _reports;

    public AdminLocationsController(IReportQueryService reports) => _reports = reports;

    [HttpGet]
    public async Task<IActionResult> List()
    {
        Guid.TryParse(User.FindFirstValue("sub"), out var adminId);
        // Admin role ⇒ all locations (the id is only used for Manager scoping, ignored here).
        var locations = await _reports.GetVisibleLocationsAsync(adminId, EmployeeRole.Admin, HttpContext.RequestAborted);
        return Ok(locations);
    }
}
