using AttendanceQR.Api.Approvals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// Change requests and the approval areas they live in. See docs/tesdiq-sistemi-faza1.md.
///
/// There is no role attribute and no [Permission] on purpose. Requests are new, so there is no «today's gate»
/// for the shadow and enforced machinery to compare against. The permission model decides every call, in every
/// AuthzMode, inside the desks (ApprovalScope). A signed-in employee with none of the request rights gets
/// nothing: an empty list, 403 or 404.
///
/// While the deployment's switch is off (ApprovalOptions, the default and production's value), every endpoint
/// answers 404 «ApprovalsDisabled» before reading anything.
///
/// A support session (impersonation) and a view-only session read, but cannot write here: /api/approvals is not
/// among ImpersonationBoundary's console write prefixes, and the view-only boundary refuses every write.
/// </summary>
[ApiController]
[Authorize]
[Route("api/approvals")]
public sealed class ApprovalsController : ControllerBase
{
    private readonly ChangeRequestDesk _desk;
    private readonly ApprovalAreaDesk _areas;

    public ApprovalsController(ChangeRequestDesk desk, ApprovalAreaDesk areas)
    {
        _desk = desk;
        _areas = areas;
    }

    private Guid Caller => User.EmployeeId();
    private CancellationToken Ct => HttpContext.RequestAborted;
    private string? Ip => HttpContext.Connection.RemoteIpAddress?.ToString();

    private IActionResult Answer(DeskResult result) => StatusCode(result.Status, result.Body);

    private IActionResult? Disabled() =>
        _desk.Enabled ? null : NotFound(new { error = "ApprovalsDisabled" });

    // GET /api/approvals/me: where the caller asks, decides or acts directly, and how many requests wait for them.
    [HttpGet("me")]
    public async Task<IActionResult> Me() =>
        _desk.Enabled ? Answer(await _desk.MeAsync(Caller, Ct)) : Ok(new { enabled = false });

    // GET /api/approvals/requests?view=queue|mine|all&status=Pending&locationId=…&limit=100
    [HttpGet("requests")]
    public async Task<IActionResult> List(
        [FromQuery] string? view, [FromQuery] string? status, [FromQuery] Guid? locationId, [FromQuery] int? limit) =>
        Disabled() ?? Answer(await _desk.ListAsync(Caller, view, status, locationId, limit, Ct));

    [HttpGet("requests/{id:guid}")]
    public async Task<IActionResult> Detail(Guid id) =>
        Disabled() ?? Answer(await _desk.DetailAsync(Caller, id, Ct));

    // POST /api/approvals/requests/employee-create: «Yeni işçi» as a request. The client sends an
    // Idempotency-Key header, and a retried press returns the first request.
    [HttpPost("requests/employee-create")]
    public async Task<IActionResult> CreateEmployee(
        [FromBody] EmployeeCreateRequestBody body, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey) =>
        Disabled() ?? Answer(await _desk.CreateEmployeeAsync(Caller, body, idempotencyKey, Ct));

    [HttpPost("requests/{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, [FromBody] DecisionBody? body) =>
        Disabled() ?? Answer(await _desk.ApproveAsync(Caller, id, body?.Note, Ct));

    // The reason is required: «Rədd səbəbi».
    [HttpPost("requests/{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] DecisionBody? body) =>
        Disabled() ?? Answer(await _desk.RejectAsync(Caller, id, body?.Note, Ct));

    [HttpPost("requests/{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] DecisionBody? body) =>
        Disabled() ?? Answer(await _desk.CancelAsync(Caller, id, body?.Note, Ct));

    // --- areas and stand-ins (Company administrator) ------------------------------------------------------

    [HttpGet("areas")]
    public async Task<IActionResult> Areas() =>
        Disabled() ?? Answer(await _areas.ListAreasAsync(Caller, Ct));

    [HttpPost("areas")]
    public async Task<IActionResult> Activate([FromBody] ApprovalAreaBody body) =>
        Disabled() ?? Answer(await _areas.ActivateAsync(Caller, body, Ip, Ct));

    [HttpDelete("areas/{id:guid}")]
    public async Task<IActionResult> Deactivate(Guid id) =>
        Disabled() ?? Answer(await _areas.DeactivateAsync(Caller, id, Ip, Ct));

    [HttpGet("substitutions")]
    public async Task<IActionResult> Substitutions() =>
        Disabled() ?? Answer(await _areas.ListSubstitutionsAsync(Caller, Ct));

    [HttpPost("substitutions")]
    public async Task<IActionResult> CreateSubstitution([FromBody] SubstitutionBody body) =>
        Disabled() ?? Answer(await _areas.CreateSubstitutionAsync(Caller, body, Ip, Ct));

    [HttpDelete("substitutions/{id:guid}")]
    public async Task<IActionResult> RevokeSubstitution(Guid id) =>
        Disabled() ?? Answer(await _areas.RevokeSubstitutionAsync(Caller, id, Ip, Ct));
}
