using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// Operational counters for the in-process queues — the only way to SEE them under load (a queue
/// that can only be inferred from missing photos is a queue nobody notices until payroll asks).
/// Numbers only, no tenant data; Admin because it is a diagnostics surface, not a public health check.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/diag")]
public class DiagController : ControllerBase
{
    private readonly IPhotoUploadQueue _photoQueue;
    private readonly IFaceMatchQueue _faceQueue;

    public DiagController(IPhotoUploadQueue photoQueue, IFaceMatchQueue faceQueue)
    {
        _photoQueue = photoQueue;
        _faceQueue = faceQueue;
    }

    [HttpGet("queues")]
    public IActionResult Queues()
    {
        var enqueued = _photoQueue.Enqueued;
        var uploaded = _photoQueue.Uploaded;
        var failed = _photoQueue.Failed;
        var depth = _photoQueue.Depth;
        return Ok(new
        {
            photo = new
            {
                depth,
                enqueued,
                uploaded,
                failed,
                // DropOldest evicts silently inside the channel, so drops are what the other
                // numbers cannot account for. Includes the rare record-deleted-before-upload no-op.
                dropped = Math.Max(0, enqueued - uploaded - failed - depth),
            },
            face = new
            {
                depth = _faceQueue.Reader.CanCount ? _faceQueue.Reader.Count : -1,
            },
        });
    }
}
