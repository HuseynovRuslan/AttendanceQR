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
    /// The random component carried in the token, populated only on success. It makes each generated
    /// token unique — it is NOT replay protection: nothing consumes it, deliberately. The QR lives on
    /// a printed poster, so refusing a repeated token would let one person per TTL window check in.
    /// Replay is bounded by the token's short TTL and by the geofence, device binding and photo.
    /// </summary>
    public string? Nonce { get; init; }

    public static QrTokenValidationResult Fail(string reason) =>
        new() { IsValid = false, FailureReason = reason };

    public static QrTokenValidationResult Success(Guid locationId, int version, string nonce) =>
        new() { IsValid = true, LocationId = locationId, Version = version, Nonce = nonce };
}
