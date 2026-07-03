namespace AttendanceQR.Infrastructure.Security;

public sealed class QrTokenValidationResult
{
    public bool IsValid { get; init; }

    public Guid? LocationId { get; init; }

    /// <summary>The version embedded in the token — the caller compares this against the
    /// location's current <c>QrVersion</c> to reject revoked (regenerated/invalidated) codes.</summary>
    public int? Version { get; init; }

    public string? FailureReason { get; init; }

    /// <summary>
    /// The token nonce, populated only on success. The caller uses it for replay
    /// protection (see <see cref="INonceStore"/>).
    /// </summary>
    public string? Nonce { get; init; }

    public static QrTokenValidationResult Fail(string reason) =>
        new() { IsValid = false, FailureReason = reason };

    public static QrTokenValidationResult Success(Guid locationId, int version, string nonce) =>
        new() { IsValid = true, LocationId = locationId, Version = version, Nonce = nonce };
}
