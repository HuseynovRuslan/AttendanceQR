namespace AttendanceQR.Infrastructure.Services;

/// <summary>
/// AWS Rekognition settings for the face-audit feature, bound from the "Rekognition" section.
/// Secrets come from environment variables. When AccessKey/SecretKey are empty the whole feature is
/// a graceful no-op (records stay <c>NotChecked</c>) — so it can be deployed "off" and enabled later.
/// </summary>
public sealed class RekognitionOptions
{
    public const string SectionName = "Rekognition";

    public string AccessKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    /// <summary>AWS region where Rekognition runs (e.g. us-east-1).</summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>Similarity (0–100) at/above which a single-face check-in counts as a match.</summary>
    public int SimilarityThreshold { get; set; } = 85;
}
