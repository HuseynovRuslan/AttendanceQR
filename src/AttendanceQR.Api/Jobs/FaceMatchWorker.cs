using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Jobs;

/// <summary>
/// Drains the face-match queue: for each enqueued check-in record, compares the selfie against the
/// employee's reference (AWS Rekognition) and stores the score + status. Fully out-of-band — nothing
/// here can affect a check-in. If Rekognition isn't configured it does nothing (records stay NotChecked).
/// </summary>
public sealed class FaceMatchWorker : BackgroundService
{
    private readonly IFaceMatchQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FaceMatchWorker> _logger;

    public FaceMatchWorker(IFaceMatchQueue queue, IServiceScopeFactory scopeFactory, ILogger<FaceMatchWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var recordId in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(recordId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FaceMatchWorker: processing {RecordId} failed", recordId);
            }
        }
    }

    private async Task ProcessAsync(Guid recordId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var face = scope.ServiceProvider.GetRequiredService<IFaceMatchService>();
        if (!face.Enabled)
            return; // feature off — leave the record NotChecked

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IPhotoStorageService>();

        var record = await db.AttendanceRecords.FirstOrDefaultAsync(r => r.Id == recordId, ct);
        if (record?.CheckInPhotoKey is null)
            return;

        var refKey = await db.Employees
            .Where(e => e.Id == record.EmployeeId)
            .Select(e => e.ReferencePhotoKey)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrEmpty(refKey))
        {
            record.FaceMatchScore = null;
            record.FaceMatchStatus = FaceMatchStatus.NoReference;
            await db.SaveChangesAsync(ct);
            return;
        }

        byte[] refBytes, chkBytes;
        try
        {
            refBytes = await storage.GetBytesAsync(refKey, ct);
            chkBytes = await storage.GetBytesAsync(record.CheckInPhotoKey, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FaceMatchWorker: could not download photos for {RecordId}", recordId);
            record.FaceMatchStatus = FaceMatchStatus.Error;
            await db.SaveChangesAsync(ct);
            return;
        }

        var outcome = await face.CompareAsync(refBytes, chkBytes, ct);
        record.FaceMatchScore = outcome.Status is FaceMatchStatus.NoFace or FaceMatchStatus.Error
            ? null
            : outcome.Score;
        record.FaceMatchStatus = outcome.Status;
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("FaceMatch {RecordId}: {Status} ({Score}%)", recordId, outcome.Status, outcome.Score);
    }
}
