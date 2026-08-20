using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Jobs;

/// <summary>
/// Drains the photo-upload queue: pushes accepted check-in selfies to R2, records the object key,
/// seeds the employee's reference selfie the first time one exists, then hands the record to the
/// face-match queue — the exact work the scan request used to do inline, minus the part where an R2
/// slowdown made 2000 phones wait on it. Fully out-of-band: nothing here can affect a check-in;
/// a failure just leaves the photo key null, same as the old best-effort semantics.
/// </summary>
public sealed class PhotoUploadWorker : BackgroundService
{
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(job, stoppingToken);
                _queue.MarkUploaded();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _queue.MarkFailed();
                _logger.LogWarning(ex, "PhotoUploadWorker: upload for record {RecordId} failed", job.RecordId);
            }
        }
    }

    private async Task ProcessAsync(PhotoUploadJob job, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        // A fresh scope has no request, so nothing has resolved the tenant — scope it to the tenant
        // the scan came from (same rule as FaceMatchWorker).
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Resolve(job.TenantId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IPhotoStorageService>();

        var record = await db.AttendanceRecords.FirstOrDefaultAsync(r => r.Id == job.RecordId, ct);
        if (record is null)
            return; // deleted in the meantime — nothing to attach a photo to

        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == job.EmployeeId, ct);
        if (employee is null)
            return;

        var nowUtc = DateTime.UtcNow;
        // Whether a reference existed BEFORE this photo — decides if there's anything to face-match
        // against (the very first photo only seeds the reference; there is nothing to compare).
        var hadReference = !string.IsNullOrEmpty(employee.ReferencePhotoKey);

        record.CheckInPhotoKey = await storage.UploadCheckInPhotoAsync(employee.Id, record.Id, job.Bytes, ct);
        record.CheckInPhotoTakenAtUtc = nowUtc;

        if (!hadReference)
        {
            employee.ReferencePhotoKey = await storage.UploadReferencePhotoAsync(employee.Id, job.Bytes, ct);
            employee.ReferencePhotoTakenAtUtc = nowUtc;
        }

        await db.SaveChangesAsync(ct);

        if (hadReference)
            _faceQueue.Enqueue(job.TenantId, record.Id);
    }
}
