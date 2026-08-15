using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AttendanceQR.Infrastructure.Services;

/// <summary>
/// MinIO-backed <see cref="IPhotoStorageService"/> using the AWS S3 SDK (path-style addressing).
/// The <see cref="IAmazonS3"/> client is configured in Program.cs to point at the MinIO endpoint.
/// </summary>
public sealed class MinioPhotoStorageService : IPhotoStorageService
{
    /// <summary>Prefix for enrollment reference selfies — never pruned by the retention job.</summary>
    public const string ReferencePrefix = "reference/";

    /// <summary>Prefix for daily check-in selfies — pruned by PhotoCleanupJob after RetentionDays.</summary>
    public const string CheckInPrefix = "checkins/";

    /// <summary>
    /// Prefix for İŞ ŞƏKLİ work-evidence photos. Separate from <see cref="CheckInPrefix"/> ON PURPOSE:
    /// PhotoCleanupJob prunes exactly CheckInPrefix, so a work photo filed under checkins/ would be
    /// deleted at 90 days along with the selfies — and this is the picture the customer wants against
    /// an invoice. FaceMatchWorker also reads checkins/ objects; a photo of a bin must never enter the
    /// face-flag queue. Nothing prunes this prefix today (see the retention note in ops).
    /// </summary>
    public const string WorkPhotoPrefix = "fieldwork/";

    // JPEG, not WebP: iOS Safari's canvas cannot encode WebP, so the client sends JPEG for
    // cross-platform capture. Older objects stored as .webp remain valid and viewable.
    private const string JpegContentType = "image/jpeg";

    private readonly IAmazonS3 _s3;
    private readonly MinioOptions _options;
    private readonly ILogger<MinioPhotoStorageService> _logger;

    public MinioPhotoStorageService(IAmazonS3 s3, IOptions<MinioOptions> options, ILogger<MinioPhotoStorageService> logger)
    {
        _s3 = s3;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> UploadCheckInPhotoAsync(Guid employeeId, Guid recordId, byte[] webpBytes, CancellationToken ct = default)
    {
        var nowUtc = DateTime.UtcNow;
        var key = $"{CheckInPrefix}{nowUtc:yyyy}/{nowUtc:MM}/{nowUtc:dd}/{employeeId}/{recordId}.jpg";
        await PutAsync(key, webpBytes, ct);
        return key;
    }

    public async Task<string> UploadReferencePhotoAsync(Guid employeeId, byte[] webpBytes, CancellationToken ct = default)
    {
        var key = $"{ReferencePrefix}{employeeId}.jpg";
        await PutAsync(key, webpBytes, ct);
        return key;
    }

    public async Task<string> UploadFieldWorkPhotoAsync(Guid tenantId, Guid visitId, byte[] jpegBytes, CancellationToken ct = default)
    {
        // The tenant id sits in the path (unlike checkins/) so a per-customer export or a lifecycle
        // rule is a prefix listing. The Guid suffix means a retried upload can never overwrite a
        // stored object.
        var nowUtc = DateTime.UtcNow;
        var key = $"{WorkPhotoPrefix}{nowUtc:yyyy}/{nowUtc:MM}/{nowUtc:dd}/{tenantId}/{visitId}-{Guid.NewGuid():N}.jpg";
        await PutAsync(key, jpegBytes, ct);
        return key;
    }


    public Task<string> GetPresignedUrlAsync(string key, CancellationToken ct = default)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddSeconds(_options.PublicUrlExpirySeconds)
        };
        // Presigning is a local HMAC computation (no network round-trip); the CancellationToken has
        // nothing to cancel here.
        return _s3.GetPreSignedURLAsync(request);
    }

    public async Task<byte[]> GetBytesAsync(string key, CancellationToken ct = default)
    {
        using var resp = await _s3.GetObjectAsync(_options.BucketName, key, ct);
        using var ms = new MemoryStream();
        await resp.ResponseStream.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    public async Task DeleteByPrefixOlderThanAsync(string prefix, DateTime olderThanUtc, CancellationToken ct = default)
    {
        string? continuationToken = null;
        var deleted = 0;
        do
        {
            var list = await _s3.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = _options.BucketName,
                Prefix = prefix,
                ContinuationToken = continuationToken
            }, ct);

            var stale = (list.S3Objects ?? new List<S3Object>())
                .Where(o => o.LastModified is DateTime m && m.ToUniversalTime() < olderThanUtc)
                .Select(o => new KeyVersion { Key = o.Key })
                .ToList();

            if (stale.Count > 0)
            {
                await _s3.DeleteObjectsAsync(new DeleteObjectsRequest
                {
                    BucketName = _options.BucketName,
                    Objects = stale
                }, ct);
                deleted += stale.Count;
            }

            continuationToken = list.IsTruncated == true ? list.NextContinuationToken : null;
        } while (continuationToken is not null);

        if (deleted > 0)
            _logger.LogInformation(
                "PhotoStorage: deleted {Count} objects under '{Prefix}' older than {Cutoff:o}", deleted, prefix, olderThanUtc);
    }

    public async Task<int> DeleteObjectsAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default)
    {
        // No endpoint configured (local dev without MinIO) — the uploads never happened either, so
        // there is nothing to remove and calling S3 would only throw.
        if (keys.Count == 0 || string.IsNullOrWhiteSpace(_options.Endpoint))
            return 0;

        var deleted = 0;
        // DeleteObjects takes at most 1000 keys per call. An employee with two years of daily
        // check-in selfies is past that on their own.
        foreach (var batch in keys.Distinct(StringComparer.Ordinal).Chunk(1000))
        {
            var response = await _s3.DeleteObjectsAsync(new DeleteObjectsRequest
            {
                BucketName = _options.BucketName,
                Objects = batch.Select(k => new KeyVersion { Key = k }).ToList(),
                // Errors are what we want back; a list of every key that succeeded is noise.
                Quiet = true
            }, ct);
            deleted += batch.Length - (response.DeleteErrors?.Count ?? 0);

            foreach (var error in response.DeleteErrors ?? new List<DeleteError>())
                _logger.LogWarning("PhotoStorage: could not delete '{Key}' — {Code} {Message}",
                    error.Key, error.Code, error.Message);
        }

        _logger.LogInformation("PhotoStorage: deleted {Deleted}/{Total} requested objects", deleted, keys.Count);
        return deleted;
    }

    private async Task PutAsync(string key, byte[] bytes, CancellationToken ct)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _options.BucketName,
            Key = key,
            InputStream = stream,
            ContentType = JpegContentType,
            // S3-compatible stores (R2/MinIO/B2) don't support the SDK v4 streaming signed/checksummed
            // payload — send a plain single-shot PUT instead. Verified against Cloudflare R2.
            UseChunkEncoding = false,
            DisableDefaultChecksumValidation = true,
        }, ct);
    }
}
