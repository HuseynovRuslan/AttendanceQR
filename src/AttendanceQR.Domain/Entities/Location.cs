namespace AttendanceQR.Domain.Entities;

public class Location
{
    public Location()
    {
        Id = Guid.NewGuid();
    }

    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public int RadiusMeters { get; set; }

    public TimeOnly ShiftStart { get; set; }

    public TimeOnly ShiftEnd { get; set; }

    public int LateThresholdMinutes { get; set; }

    /// <summary>
    /// When false the location is temporarily disabled: the kiosk stops issuing QR tokens and
    /// scans are rejected, but all data (employees, history) is preserved. Defaults to true.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Every issued QR token (rotating kiosk or long-lived printable) embeds this version. Scan
    /// only accepts a token whose embedded version matches the current value here, so bumping it
    /// instantly invalidates every outstanding QR for this location — the kiosk's next 55s refresh
    /// picks up the new version automatically; any printed poster becomes unscannable immediately.
    /// </summary>
    public int QrVersion { get; set; }
}
