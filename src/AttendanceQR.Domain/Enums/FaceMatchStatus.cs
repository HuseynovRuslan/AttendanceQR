namespace AttendanceQR.Domain.Enums;

/// <summary>
/// Result of comparing a check-in selfie against the employee's reference photo (AWS Rekognition).
/// Purely advisory — it flags records for a manager to review, never blocks a check-in.
/// </summary>
public enum FaceMatchStatus
{
    /// <summary>Not compared yet (no photo, feature off, or pending in the background queue).</summary>
    NotChecked = 0,

    /// <summary>One clear face, similarity above the threshold — looks like the right person.</summary>
    Ok = 1,

    /// <summary>One face but similarity below the threshold — likely a different person. FLAG.</summary>
    Mismatch = 2,

    /// <summary>More than one face in the check-in photo (a crowd) — ambiguous. FLAG.</summary>
    MultiFace = 3,

    /// <summary>No face detected in the check-in photo. FLAG.</summary>
    NoFace = 4,

    /// <summary>The employee has no reference photo to compare against yet.</summary>
    NoReference = 5,

    /// <summary>Comparison failed (bad reference, service error) — retry via re-check.</summary>
    Error = 6
}
