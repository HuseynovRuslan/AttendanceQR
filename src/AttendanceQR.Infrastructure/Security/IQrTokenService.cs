namespace AttendanceQR.Infrastructure.Security;

public interface IQrTokenService
{
    /// <summary>
    /// Issues a signed token for a location, embedding <paramref name="version"/> (must match the
    /// location's current <c>QrVersion</c> at scan time) and expiring after
    /// <paramref name="ttlSeconds"/> (defaults to <see cref="QrTokenOptions.TtlSeconds"/> — the
    /// kiosk's short rotation window; callers pass a longer value for a printable/static code).
    /// </summary>
    string Generate(Guid locationId, int version, int? ttlSeconds = null);

    /// <summary>
    /// Issues a signed token with no time expiry. The location/version pair is still embedded and
    /// validated by the scan path, so bumping <c>Location.QrVersion</c> revokes it like any other QR.
    /// </summary>
    string GeneratePermanent(Guid locationId, int version);

    QrTokenValidationResult Validate(string token);
}
