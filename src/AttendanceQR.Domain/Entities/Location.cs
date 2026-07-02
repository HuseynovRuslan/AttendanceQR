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
}
