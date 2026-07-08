namespace AttendanceQR.Infrastructure.Services;

/// <summary>
/// MinIO / S3-compatible object storage settings, bound from the "Storage:Minio" section.
/// Secrets (<see cref="AccessKey"/>/<see cref="SecretKey"/>) must come from environment variables or
/// user-secrets — never hard-coded in appsettings.json (which ships with empty placeholders).
/// When <see cref="Endpoint"/> is empty the photo-audit feature degrades gracefully to a no-op.
/// </summary>
public sealed class MinioOptions
{
    public const string SectionName = "Storage:Minio";

    /// <summary>Host[:port] of the MinIO server, WITHOUT scheme (scheme is derived from <see cref="UseSsl"/>).</summary>
    public string Endpoint { get; set; } = string.Empty;

    public string AccessKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    public string BucketName { get; set; } = "attendance-photos";

    /// <summary>https when true, http when false. MinIO behind Coolify's proxy is typically https.</summary>
    public bool UseSsl { get; set; } = true;

    /// <summary>
    /// SigV4 signing region for the custom endpoint. Leave empty for MinIO (region is ignored).
    /// Set to <c>auto</c> for Cloudflare R2, or the bucket region for AWS S3 / Backblaze B2.
    /// </summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>Lifetime of the presigned view URLs handed to the admin panel.</summary>
    public int PublicUrlExpirySeconds { get; set; } = 300;

    /// <summary>Daily check-in photos older than this are deleted by PhotoCleanupJob. Reference photos are never touched.</summary>
    public int RetentionDays { get; set; } = 90;
}
