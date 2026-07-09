namespace AttendanceQR.Infrastructure.Services;

/// <summary>
/// Object-storage access for the photo-audit feature. Selfies live in MinIO (S3-compatible); the
/// database only stores object keys. Two prefixes:
/// <list type="bullet">
/// <item><c>reference/{employeeId}.webp</c> — the enrollment reference selfie, kept indefinitely.</item>
/// <item><c>checkins/{yyyy}/{MM}/{dd}/{employeeId}/{recordId}.webp</c> — daily check-in selfie, retained ~90 days.</item>
/// </list>
/// </summary>
public interface IPhotoStorageService
{
    /// <summary>Uploads a daily check-in selfie; returns the object key to store on the record.</summary>
    Task<string> UploadCheckInPhotoAsync(Guid employeeId, Guid recordId, byte[] webpBytes, CancellationToken ct = default);

    /// <summary>Uploads (or overwrites) the employee's reference selfie; returns the object key.</summary>
    Task<string> UploadReferencePhotoAsync(Guid employeeId, byte[] webpBytes, CancellationToken ct = default);

    /// <summary>A short-lived presigned GET URL the admin panel can load the image from directly.</summary>
    Task<string> GetPresignedUrlAsync(string key, CancellationToken ct = default);

    /// <summary>Downloads an object's raw bytes (used by the face-audit worker to feed Rekognition).</summary>
    Task<byte[]> GetBytesAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Deletes every object under <paramref name="prefix"/> last modified before
    /// <paramref name="olderThanUtc"/>. Used by the retention job — never point it at <c>reference/</c>.
    /// </summary>
    Task DeleteByPrefixOlderThanAsync(string prefix, DateTime olderThanUtc, CancellationToken ct = default);
}
