using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Jobs;

/// <summary>
/// Uploads check-in selfies from the DURABLE queue (PendingPhotoUploads) to object storage.
///
/// Two feeds, one processor: the in-memory hint channel starts a healthy upload milliseconds after
/// the scan; the poller sweeps the table for anything due — rows whose hint was lost to a restart,
/// and rows waiting out their retry backoff. Every row ends in exactly one of: uploaded (row
/// deleted), permanently failed after the retry budget (row deleted, ERROR log), or dropped because
/// its attendance record vanished. Nothing is ever lost silently: a deploy mid-queue just means the
/// next process's poller picks the rows back up from the database.
///
/// Twelve lanes because the 2000-employee load test measured a single consumer at ~4 uploads/s
/// against a 6.7/s arrival burst; uploads are independent I/O and each job gets its own DI scope.
/// </summary>
public sealed class PhotoUploadWorker : BackgroundService
{
    private const int Parallelism = 12;
    private static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(20);
    private const int PollBatchPerTenant = 300;

    // ~30s → 5min exponential backoff, capped so recovery after a long outage starts within
    // minutes of R2 returning, not hours. 60 attempts ≈ 5 hours of continuous outage covered;
    // past that the photo is declared failed LOUDLY (and the age cleanup is the final backstop).
    // Public only so the schedule can be pinned by tests — nothing else may call it.
    public const int MaxAttempts = 60;
    public static TimeSpan BackoffFor(int attempts) =>
        TimeSpan.FromSeconds(Math.Min(300, 30 * Math.Pow(2, Math.Clamp(attempts - 1, 0, 4))));

    private readonly IPhotoUploadQueue _queue;
    private readonly IFaceMatchQueue _faceQueue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PhotoUploadWorker> _logger;

    public PhotoUploadWorker(
        IPhotoUploadQueue queue, IFaceMatchQueue faceQueue, IServiceScopeFactory scopeFactory,
        ILogger<PhotoUploadWorker> logger)
    {
        _queue = queue;
        _faceQueue = faceQueue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(Enumerable.Range(0, Parallelism)
            .Select(_ => ConsumeHintsAsync(stoppingToken))
            .Append(PollAsync(stoppingToken)));

    private async Task ConsumeHintsAsync(CancellationToken ct)
    {
        await foreach (var hint in _queue.Reader.ReadAllAsync(ct))
            await ProcessSafeAsync(hint.TenantId, hint.PendingId, ct);
    }

    // The durability half: finds due rows regardless of how this process started or what the
    // channel lost, reconciles the approximate pending total the enqueue cap reads, and re-hints
    // due work into the lanes.
    private async Task PollAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                List<Guid> tenantIds;
                using (var listScope = _scopeFactory.CreateScope())
                {
                    var db = listScope.ServiceProvider.GetRequiredService<AppDbContext>();
                    tenantIds = await db.Tenants.Where(t => t.IsActive).Select(t => t.Id).ToListAsync(ct);
                }

                long totalPending = 0;
                var nowUtc = DateTime.UtcNow;
                foreach (var tenantId in tenantIds)
                {
                    using var scope = _scopeFactory.CreateScope();
                    scope.ServiceProvider.GetRequiredService<ITenantContext>().Resolve(tenantId);
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    totalPending += await db.PendingPhotoUploads.CountAsync(ct);

                    var due = await db.PendingPhotoUploads
                        .Where(p => p.NextAttemptUtc <= nowUtc)
                        .OrderBy(p => p.NextAttemptUtc)
                        .Select(p => p.Id)
                        .Take(PollBatchPerTenant)
                        .ToListAsync(ct);
                    foreach (var id in due)
                        _queue.HintReady(new PhotoUploadHint(tenantId, id));
                }
                _queue.PendingReconcile(totalPending);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PhotoUploadWorker: poll sweep failed");
            }

            try
            {
                await Task.Delay(PollEvery, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ProcessSafeAsync(Guid tenantId, Guid pendingId, CancellationToken ct)
    {
        // The hint path and the poller can both name the same row — first claim wins, the other
        // walks away. The claim is process-local, which is enough: only one process runs.
        if (!_queue.TryBeginInFlight(pendingId))
            return;
        try
        {
            await ProcessAsync(tenantId, pendingId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown mid-upload: the row is untouched in the DB; the next process retries it.
        }
        catch (Exception ex)
        {
            // Unexpected infrastructure failure OUTSIDE the upload try (scope/DB). The row survives;
            // the poller will come back to it.
            _logger.LogWarning(ex, "PhotoUploadWorker: processing pending {PendingId} failed", pendingId);
        }
        finally
        {
            _queue.EndInFlight(pendingId);
        }
    }

    private async Task ProcessAsync(Guid tenantId, Guid pendingId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Resolve(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IPhotoStorageService>();

        var pending = await db.PendingPhotoUploads.FirstOrDefaultAsync(p => p.Id == pendingId, ct);
        if (pending is null)
            return; // already uploaded by an earlier claim — nothing to do
        if (pending.NextAttemptUtc > DateTime.UtcNow)
            return; // a stale hint fired early — the poller owns the schedule

        var record = await db.AttendanceRecords.FirstOrDefaultAsync(r => r.Id == pending.RecordId, ct);
        var employee = record is null ? null
            : await db.Employees.FirstOrDefaultAsync(e => e.Id == pending.EmployeeId, ct);
        if (record is null || employee is null)
        {
            // The attendance record (or its employee) was deleted while the photo waited — there is
            // nothing left to attach it to. Counted, logged, never retried.
            db.PendingPhotoUploads.Remove(pending);
            await db.SaveChangesAsync(ct);
            _queue.PendingDelta(-1);
            _queue.MarkDropped();
            _logger.LogInformation("PhotoUploadWorker: pending {PendingId} dropped — record gone", pendingId);
            return;
        }

        try
        {
            var nowUtc = DateTime.UtcNow;
            var hadReference = !string.IsNullOrEmpty(employee.ReferencePhotoKey);

            record.CheckInPhotoKey = await storage.UploadCheckInPhotoAsync(employee.Id, record.Id, pending.Bytes, ct);
            record.CheckInPhotoTakenAtUtc = nowUtc;
            // A QR-less check-in has already looked at this photo (CompareFaceNowAsync): a frame with
            // no face, or with a crowd in it, must not become the face everything after is judged by.
            if (!hadReference && !FaceVerdicts.UnfitAsReference(record.FaceMatchStatus))
            {
                employee.ReferencePhotoKey = await storage.UploadReferencePhotoAsync(employee.Id, pending.Bytes, ct);
                employee.ReferencePhotoTakenAtUtc = nowUtc;
            }

            db.PendingPhotoUploads.Remove(pending);
            await db.SaveChangesAsync(ct);
            _queue.PendingDelta(-1);
            _queue.MarkUploaded();

            // The worker is the retry path for a verdict not yet decided — never a second opinion on one
            // that was (see FaceVerdicts): a QR-less check-in decided its face inside the request.
            if (hadReference && !FaceVerdicts.IsDecided(record.FaceMatchStatus))
                _faceQueue.Enqueue(tenantId, record.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            pending.Attempts++;
            if (pending.Attempts >= MaxAttempts)
            {
                // The retry budget (~5h of outage) is spent. Loud, counted, and the bytes released.
                db.PendingPhotoUploads.Remove(pending);
                await db.SaveChangesAsync(ct);
                _queue.PendingDelta(-1);
                _queue.MarkFailed();
                _logger.LogError(ex,
                    "PhotoUploadWorker: pending {PendingId} FAILED PERMANENTLY after {Attempts} attempts (record {RecordId})",
                    pendingId, pending.Attempts, pending.RecordId);
            }
            else
            {
                pending.NextAttemptUtc = DateTime.UtcNow + BackoffFor(pending.Attempts);
                await db.SaveChangesAsync(ct);
                _queue.MarkRetry();
                _logger.LogWarning(
                    "PhotoUploadWorker: upload for pending {PendingId} failed (attempt {Attempts}) — retry at {Next:o}: {Error}",
                    pendingId, pending.Attempts, pending.NextAttemptUtc, ex.Message);
            }
        }
    }
}
