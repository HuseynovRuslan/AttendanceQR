namespace AttendanceQR.Application.Common;

/// <summary>
/// How far an OFFLINE scan may report itself from its branch and still be recorded.
///
/// A scan taken with no signal is judged later, when the phone finds one — and the position it
/// carries is whatever the phone could tell without help: no Wi-Fi or cell lookup, often the last fix
/// it took on the way in. The Balaxanı warehouse showed it plainly on 27.10.2026: 38 of 47 refused
/// replays sat in one spot 300 m south, on the street people walk in by, while the same people's live
/// scans stand at the poster. Refusing such a replay costs a day that was worked, hours after the
/// person could have done anything about it — across the company that was 99 lost days in three weeks.
///
/// So within <see cref="SlackMeters"/> of the branch the replay is recorded, and written down as a scan
/// from outside (<c>CheckInOutsideFence</c>, the same measurement a branch with its fence switched off
/// makes), so the board and the Problems map show it and a manager can void it. Beyond that it is
/// refused exactly as before: fifteen kilometres is not a stale fix, it is a photograph of the poster.
///
/// Live scans are untouched — the employee is standing there and can simply try again.
/// </summary>
public static class OfflineFence
{
    /// <summary>Measured from the branch's centre, not from the edge of its circle (owner, 27.10.2026).</summary>
    public const double SlackMeters = 1500;

    /// <summary>
    /// True when a scan that fell outside its branch's circle is still to be recorded. Only a replay
    /// that carries the phone's own clock qualifies: that is what the offline queue always sends, and a
    /// request without it was never stored on a phone at all.
    /// </summary>
    public static bool Records(bool offline, bool carriesPhoneTime, double distanceMeters) =>
        offline && carriesPhoneTime && distanceMeters <= SlackMeters;
}
