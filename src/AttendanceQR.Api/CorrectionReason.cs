namespace AttendanceQR.Api;

/// <summary>
/// The owner's rule of 2026-10-15: a correction to somebody's attendance says why, and the why is kept —
/// in the audit line written beside the change. Validated by hand, not by an attribute on the request
/// record: a validation attribute on a record's positional property once turned every scan into a 500
/// (see the record-validation note), and these requests sit next to the scan path.
/// </summary>
public static class CorrectionReason
{
    public const int Min = 3;
    public const int Max = 500;
    public const string Required = "ReasonRequired";

    /// <summary>The reason as it will be stored, or null when it is missing, too short or too long.</summary>
    public static string? Clean(string? reason)
    {
        var trimmed = reason?.Trim();
        return trimmed is { Length: >= Min and <= Max } ? trimmed : null;
    }
}
