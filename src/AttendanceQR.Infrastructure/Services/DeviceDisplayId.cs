using System.Security.Cryptography;
using System.Text;

namespace AttendanceQR.Infrastructure.Services;

/// <summary>
/// What the admin screens show for a device instead of its fingerprint.
///
/// The fingerprint is not an identifier, it is a credential: the scan trusts it as "this person's phone",
/// and forgot-pin/verify treated it as the possession factor of a PIN reset. Every admin of a company, and
/// every manager over a branch, used to receive it raw from the device screens — enough to present
/// somebody else's phone without holding it. A screen only needs to tell devices apart and to group the
/// accounts on one handset, so it gets the first 8 hex digits of the fingerprint's SHA-256: stable,
/// short enough to read out on the phone, and of no use to anybody who would rather be that device.
///
/// Fingerprints are random UUIDs minted on the device (lib/device.ts), so the hash cannot be walked back
/// by guessing inputs. Server-side matching and grouping keep using the real value; this is for display.
/// </summary>
public static class DeviceDisplayId
{
    public static string Of(string fingerprint)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint));
        return Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    }

    public static string? OfOrNull(string? fingerprint)
        => string.IsNullOrEmpty(fingerprint) ? null : Of(fingerprint);
}
