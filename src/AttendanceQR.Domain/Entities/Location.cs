namespace AttendanceQR.Domain.Entities;

public class Location : ITenantScoped
{
    public Location()
    {
        Id = Guid.NewGuid();
    }

    public Guid Id { get; set; }

    // Multi-tenancy: which company (Tenant) this row belongs to.
    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public int RadiusMeters { get; set; }

    /// <summary>
    /// When this branch's geofence was last moved, by whom, and by how many metres.
    ///
    /// The geofence is the anti-fraud boundary: everything the product claims about somebody being at
    /// work rests on it. Until now it could be changed by anyone with the screen open and nothing
    /// anywhere recorded it — not the time, not the person, not the distance — so a fence quietly
    /// moved onto somebody's house would have looked exactly like a fence that was always there.
    ///
    /// Now that a branch MANAGER may correct their own coordinates (they know the site; the admin was
    /// guessing), the move has to be visible. It is not prevented — refusing the edit would keep nine
    /// sites on coordinates nobody has ever checked — it is recorded, stamped on the branch itself so
    /// the list can show it, and written to the audit log in full.
    /// </summary>
    public DateTime? GeofenceMovedAtUtc { get; set; }

    public Guid? GeofenceMovedByEmployeeId { get; set; }

    /// <summary>How far the centre moved, in metres. Null when it has never been moved.</summary>
    public int? GeofenceMovedMeters { get; set; }

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

    /// <summary>
    /// Bitmask of which days of the week are working days, indexed by .NET's DayOfWeek
    /// (Sunday=0 ... Saturday=6): bit set = working day. Default 126 (0b1111110) = every day
    /// except Sunday. A day that isn't a working day here (or is listed in NonWorkingDay) shows
    /// DailySummaryStatus.DayOff instead of Absent when nobody checked in.
    /// </summary>
    public int WorkDaysMask { get; set; } = 126;

    /// <summary>
    /// This branch has no QR poster — there is nowhere to hang one (a stretch of road, a stadium's
    /// surroundings) — so its staff check in from the home screen with a selfie and GPS instead.
    ///
    /// A property of the BRANCH, deliberately not of the employee. The poster's absence is a fact
    /// about the place: everyone posted there is in the same position, and a new hire assigned here
    /// inherits it without anybody remembering to tick a box on their card. Nineteen people at two
    /// such branches had spent five weeks invisible to the system — not because a per-person
    /// permission was missing (it had been granted to all of them) but because nothing about the
    /// place told the app there was no poster to scan.
    ///
    /// What it changes on a scan: the QR token may be empty, the employee's OWN branch stands in for
    /// the one a poster would name, the geofence is that branch's, and the face is compared before
    /// the reply rather than in the background — with no poster the selfie is the only anchor left.
    /// It never loosens the fence and never lets the phone choose a different branch.
    /// </summary>
    public bool QrlessCheckIn { get; set; }

    /// <summary>
    /// Does the radius REFUSE a scan from outside it, or merely measure it? True — the default and
    /// every existing branch — is the fence as it has always been.
    ///
    /// Switched off for a site nobody has ever clocked in at. «Socar-1 (Aeroport yolu)» is the case
    /// it was built for: fourteen staff, a 150 m circle dropped on a stretch of road, and four
    /// check-ins in the system's whole history — so there is no position data to size a circle from
    /// and no way to get any while the circle is what blocks people. With this off the scan is
    /// recorded wherever it happens and the distance is written to the audit as
    /// <see cref="AuditEventType.CheckInOutsideFence"/>, which the Problems screen maps. After a week
    /// of real points the centre and radius are set from evidence, and the fence goes back up.
    ///
    /// It is deliberately a property of the BRANCH and deliberately NOT permanent: a site with the
    /// wall down has no location gate at all, so at a branch that also has no poster
    /// (<see cref="QrlessCheckIn"/>) the only remaining anchor is the selfie and its face match.
    /// The admin form says so in those words.
    /// </summary>
    public bool RequireGeofence { get; set; } = true;
}
