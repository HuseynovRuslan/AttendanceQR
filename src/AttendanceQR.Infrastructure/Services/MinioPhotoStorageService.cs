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

    private const string WebpContentType = "image/webp";

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
        var key = $"{CheckInPrefix}{nowUtc:yyyy}/{nowUtc:MM}/{nowUtc:dd}/{employeeId}/{recordId}.webp";
        await PutAsync(key, webpBytes, ct);
        return key;
    }

    public async Task<string> UploadReferencePhotoAsync(Guid employeeId, byte[] webpBytes, CancellationToken ct = default)
    {
        var key = $"{ReferencePrefix}{employeeId}.webp";
        await PutAsync(key, webpBytes, ct);
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

    private async Task PutAsync(string key, byte[] bytes, CancellationToken ct)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _options.BucketName,
            Key = key,
            InputStream = stream,
            ContentType = WebpContentType,
            // S3-compatible stores (R2/MinIO/B2) don't support the SDK v4 streaming signed/checksummed
            // payload — send a plain single-shot PUT instead. Verified against Cloudflare R2.
            UseChunkEncoding = false,
            DisableDefaultChecksumValidation = true,
        }, ct);
    }
}
