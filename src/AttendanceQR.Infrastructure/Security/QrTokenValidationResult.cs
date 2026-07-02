namespace AttendanceQR.Infrastructure.Security;

public sealed class QrTokenValidationResult
{
    public bool IsValid { get; init; }

    public Guid? LocationId { get; init; }

    public string? FailureReason { get; init; }

    /// <summary>
    /// The token nonce, populated only on success. The caller uses it for replay
    /// protection (see <see cref="INonceStore"/>).
    /// </summary>
    public string? Nonce { get; init; }

    public static QrTokenValidationResult Fail(string reason) =>
        new() { IsValid = false, FailureReason = reason };

    public static QrTokenValidationResult Success(Guid locationId, string nonce) =>
        new() { IsValid = true, LocationId = locationId, Nonce = nonce };
}
