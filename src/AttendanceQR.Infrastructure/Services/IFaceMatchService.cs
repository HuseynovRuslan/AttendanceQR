using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Infrastructure.Services;

public sealed record FaceMatchOutcome(int Score, int FaceCount, FaceMatchStatus Status);

/// <summary>
/// Compares a check-in selfie against an employee's reference photo (AWS Rekognition CompareFaces).
/// Advisory only — the caller stores the result to flag suspicious records; it never blocks check-in.
/// </summary>
public interface IFaceMatchService
{
    /// <summary>False when no AWS credentials are configured — callers should skip and leave NotChecked.</summary>
    bool Enabled { get; }

    Task<FaceMatchOutcome> CompareAsync(byte[] referenceBytes, byte[] checkInBytes, CancellationToken ct = default);
}
