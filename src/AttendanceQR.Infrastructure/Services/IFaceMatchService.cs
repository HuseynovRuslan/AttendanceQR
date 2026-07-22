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

    /// <summary>
    /// How many faces are in one photo — no reference needed, no comparison, just "is anybody there".
    ///
    /// Separate from CompareAsync because it answers a question that must be answered WHILE the
    /// employee is still on the scan screen: comparing needs their reference photo fetched from
    /// storage and costs more, and the retake prompt only cares whether a face is present at all.
    /// Returns -1 when the answer is unknown (feature off, service error) — callers must treat that
    /// as "don't warn" rather than as zero.
    /// </summary>
    Task<int> DetectFaceCountAsync(byte[] photoBytes, CancellationToken ct = default);
}
