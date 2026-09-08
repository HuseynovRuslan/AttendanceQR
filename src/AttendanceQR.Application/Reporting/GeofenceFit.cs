namespace AttendanceQR.Application.Reporting;

/// <summary>
/// Does a site's GPS circle match the ground it actually covers?
///
/// A branch is stored as one point and one radius, which is right for a shop and wrong for a park, a
/// bridge or a stretch of road. When it is wrong nothing breaks and nobody is told: the scan is
/// refused, the worker taps four more times, gives up, and the day is written as Qayıb. The refusals
/// are all in the audit log — they were never on a screen.
///
/// Measured on 2026-09-08 across forty days: Qafur Məmmədov Parkı refused 133 scans, the CLOSEST of
/// them from 466 metres against a 150-metre circle; Heydər Əliyev Mərkəzi 88, the closest from
/// exactly 500 against a 500-metre circle. Nobody was cheating in either case — the crews work where
/// the work is, and the circle was drawn somewhere else.
///
/// This decides nothing. It reports the spread and names the shape of the problem, because the two
/// shapes need opposite fixes: a circle a few metres too tight is widened, and a circle in the wrong
/// PLACE is moved — widening that one would licence scanning from home.
/// </summary>
public static class GeofenceFit
{
    /// <summary>Fewer refusals than this is noise — one person, one bad GPS fix, one wrong day.</summary>
    public const int MinRejections = 5;

    /// <summary>
    /// Within this multiple of the radius, a refusal is a near miss: the person was at the site and
    /// the circle (or the phone's accuracy) was a few metres short. A phone is routinely 20–50 m out.
    /// </summary>
    public const double NearMissFactor = 1.5;

    public enum Verdict
    {
        /// <summary>Nothing worth asking about.</summary>
        Ok,

        /// <summary>
        /// People are being refused from just outside the line — the circle is a little too tight for
        /// the site and for GPS accuracy. Widening it is the fix.
        /// </summary>
        Tight,

        /// <summary>
        /// Every refusal is far outside. Either the crew works somewhere else entirely (a park, a road)
        /// or the site's registered point is wrong. Widening a circle that is in the wrong PLACE would
        /// licence scanning from anywhere, so this one is moved, not stretched.
        /// </summary>
        Misplaced,
    }

    /// <param name="nearestRejectedMeters">The closest anybody was when refused — the single most
    /// telling number here. If even the nearest refusal is far out, the circle is not merely tight.</param>
    public static Verdict Judge(int radiusMeters, int rejections, double? nearestRejectedMeters)
    {
        if (rejections < MinRejections || nearestRejectedMeters is not double nearest)
            return Verdict.Ok;

        return nearest <= radiusMeters * NearMissFactor ? Verdict.Tight : Verdict.Misplaced;
    }
}
