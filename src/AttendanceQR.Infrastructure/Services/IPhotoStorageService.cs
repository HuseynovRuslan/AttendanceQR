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

    /// <summary>
    /// Uploads a field visit's İŞ ŞƏKLİ — a photo of the WORK, not of a face. Filed under its own
    /// <c>fieldwork/</c> prefix so the retention job (which prunes selfies) and the face-match worker
    /// (which reads them) never touch it. Returns the object key.
    /// </summary>
    Task<string> UploadFieldWorkPhotoAsync(Guid tenantId, Guid visitId, byte[] jpegBytes, CancellationToken ct = default);

    /// <summary>
    /// Uploads a task's proof photo — a picture of the WORK, filed under its own <c>tasks/</c> prefix
    /// for the same reason the field-work photo has one: the retention job prunes selfies and the
    /// face-match worker reads them, and neither has any business with a photo of a swept corridor.
    /// </summary>
    Task<string> UploadTaskPhotoAsync(Guid tenantId, Guid taskId, byte[] jpegBytes, CancellationToken ct = default);

    /// <summary>A short-lived presigned GET URL the admin panel can load the image from directly.</summary>
    Task<string> GetPresignedUrlAsync(string key, CancellationToken ct = default);

    /// <summary>Downloads an object's raw bytes (used by the face-audit worker to feed Rekognition).</summary>
    Task<byte[]> GetBytesAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Deletes every object under <paramref name="prefix"/> last modified before
    /// <paramref name="olderThanUtc"/>. Used by the retention job — never point it at <c>reference/</c>.
    /// </summary>
    Task DeleteByPrefixOlderThanAsync(string prefix, DateTime olderThanUtc, CancellationToken ct = default);

    /// <summary>
    /// Deletes the named objects. Used when an employee is deleted: their rows go, but their face
    /// stayed in the bucket — <c>reference/</c> is excluded from the retention job on purpose, so a
    /// deleted employee's enrollment selfie was kept for ever. qrlog.az/hesab-silinmesi/ publishes
    /// that a deletion request removes "referans (profil) şəkli və giriş/çıxış anındakı selfilər",
    /// which made this the difference between a promise and a fact.
    ///
    /// Returns how many were removed. Missing keys are not an error — S3 delete is idempotent, and
    /// half of these objects may already have aged out of the retention window.
    /// </summary>
    Task<int> DeleteObjectsAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default);
}
