using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
    private readonly Infrastructure.Persistence.AppDbContext _db;

    public DiagController(IPhotoUploadQueue photoQueue, IFaceMatchQueue faceQueue, Infrastructure.Persistence.AppDbContext db)
    {
        _photoQueue = photoQueue;
        _faceQueue = faceQueue;
        _db = db;
    }

    [HttpGet("queues")]
    public async Task<IActionResult> Queues()
    {
        var ct = HttpContext.RequestAborted;
        var nowUtc = DateTime.UtcNow;
        // The durable queue's truth, for the CALLER's tenant (query-filtered): rows waiting, and how
        // many of them are due now vs sitting out a retry backoff.
        var pending = await _db.PendingPhotoUploads.CountAsync(ct);
        var due = await _db.PendingPhotoUploads.CountAsync(p => p.NextAttemptUtc <= nowUtc, ct);

        return Ok(new
        {
            photo = new
            {
                // Process-lifetime counters, all tenants. Every accepted photo ends in exactly one
                // of uploaded / failed (permanent) / dropped — queued+retrying are the in-between.
                queued = _photoQueue.Enqueued,
                uploaded = _photoQueue.Uploaded,
                retries = _photoQueue.Retries,
                failed = _photoQueue.Failed,
                dropped = _photoQueue.Dropped,
                pendingAllTenants = _photoQueue.PendingApprox,
                // This tenant's durable rows.
                pending,
                due,
            },
            face = new
            {
                depth = _faceQueue.Reader.CanCount ? _faceQueue.Reader.Count : -1,
            },
        });
    }
}
