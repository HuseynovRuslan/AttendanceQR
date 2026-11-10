using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Infrastructure.Services;

/// <summary>
/// The rules of presence checks («Mövcudluq yoxlaması», see <see cref="Domain.Entities.PresenceCheck"/>) — the one
/// place they are decided. Agreed with the owner on 10.11.2026; change them there first.
///
/// Pure on purpose: the worker, the answer endpoint and the tests all ask the same functions, so «when may it be
/// asked» and «was that inside» cannot drift apart between them.
/// </summary>
public static class PresenceCheckRules
{
    /// <summary>At most this many checks on one company day, whatever the shifts.</summary>
    public const int MaxPerDay = 2;

    /// <summary>Nothing in the first minutes after arriving: the person is still taking their coat off.</summary>
    public static readonly TimeSpan SettleAfterCheckIn = TimeSpan.FromMinutes(20);

    /// <summary>Nothing in the last minutes of the shift: a person packing up to leave is not a person who left.</summary>
    public static readonly TimeSpan QuietBeforeShiftEnd = TimeSpan.FromMinutes(20);

    /// <summary>Two checks on the same day are at least this far apart.</summary>
    public static readonly TimeSpan MinGap = TimeSpan.FromMinutes(60);

    /// <summary>
    /// How long an answer is «on time». After it the check reads «cavab yoxdur» — but a late answer is still taken
    /// and replaces it, marked late: the position is the point, and a person who finishes the shelf first and
    /// then answers from the shop floor was in the shop.
    /// </summary>
    public static readonly TimeSpan AnswerWindow = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The window used when the shift's end says nothing about this day — the person checked in after their
    /// shift had already ended (overtime, a cover nobody entered). Four hours from when checks may start is long
    /// enough to be unpredictable and short enough not to ask at midnight after they forgot to check out.
    /// </summary>
    public static readonly TimeSpan FallbackWindow = TimeSpan.FromHours(4);

    /// <summary>A shift end further than this from the check-in is not this day's end; the fallback is used.</summary>
    public static readonly TimeSpan LongestShift = TimeSpan.FromHours(16);

    /// <summary>
    /// A planned check that is overdue by more than this is not sent at all. A deploy restart costs a couple of
    /// minutes and the check still goes; an outage of an hour would otherwise fire a burst of stale checks at
    /// people near the end of their day.
    /// </summary>
    public static readonly TimeSpan SendTolerance = TimeSpan.FromMinutes(30);

    /// <summary>The durations a manager may choose. There is no «until switched off».</summary>
    public static readonly int[] AllowedDays = [7, 14, 30];

    /// <summary>What the person reads. Neutral: no «şübhə», no «nəzarət» — it asks, it does not accuse.</summary>
    public const string PushTitle = "Yerinizi təsdiqləyin";
    public const string PushBody = "Tətbiqi açıb düyməni sıxın. 15 dəqiqə vaxtınız var.";

    // Why a check was cancelled (PresenceCheck.Note).
    public const string NoteDayClosed = "DayClosed";
    public const string NoteFieldVisit = "FieldVisit";
    public const string NoteDisabled = "Disabled";
    public const string NoteStale = "Stale";
    public const string NoteInactive = "Inactive";

    /// <summary>The phone's own reasons for giving no position — the same set the scan's failure report accepts.</summary>
    public static readonly string[] GpsFailureKinds = ["denied", "unavailable", "timeout", "unsupported"];

    public static bool IsOn(DateTime? untilUtc, DateTime nowUtc) => untilUtc is DateTime u && u > nowUtc;

    /// <summary>
    /// When checks may be asked on a day that opened at <paramref name="checkInUtc"/>: from twenty minutes after
    /// arriving (or from now, when they were switched on later) until twenty minutes before the shift ends.
    /// <paramref name="shiftEndUtc"/> is the end of the shift the check-in belongs to, or null when the person
    /// checked in outside their hours — then <see cref="FallbackWindow"/>. Null when there is no room left.
    /// </summary>
    public static (DateTime Start, DateTime End)? Window(DateTime checkInUtc, DateTime nowUtc, DateTime? shiftEndUtc)
    {
        var start = checkInUtc + SettleAfterCheckIn;
        var soonest = nowUtc + TimeSpan.FromMinutes(1);
        if (start < soonest)
            start = soonest;

        var end = shiftEndUtc is DateTime shiftEnd && shiftEnd > checkInUtc && shiftEnd - checkInUtc <= LongestShift
            ? shiftEnd - QuietBeforeShiftEnd
            : start + FallbackWindow;
        return end > start ? (start, end) : null;
    }

    /// <summary>
    /// <paramref name="count"/> moments (at most <see cref="MaxPerDay"/>) at random inside the window, at least
    /// <see cref="MinGap"/> apart. Two are drawn uniformly over every pair the gap allows — not «the first anywhere,
    /// the second after it», which would bunch the first toward the start of the day. A window too short for the
    /// gap gets one.
    /// </summary>
    public static IReadOnlyList<DateTime> PlanTimes(DateTime start, DateTime end, int count, Random random)
    {
        count = Math.Min(count, MaxPerDay);
        if (count <= 0 || end <= start)
            return [];

        var minutes = (end - start).TotalMinutes;
        var gap = MinGap.TotalMinutes;
        if (count == 1 || minutes < gap)
            return [start.AddMinutes(random.NextDouble() * minutes)];

        var room = minutes - gap;
        var a = random.NextDouble() * room;
        var b = random.NextDouble() * room;
        if (a > b)
            (a, b) = (b, a);
        return [start.AddMinutes(a), start.AddMinutes(b + gap)];
    }

    /// <summary>
    /// Is this a position at all? The geofence's own lesson (see ScanRequest): a non-finite coordinate makes the
    /// distance NaN, and every comparison against NaN is false — «not further than the radius» — so an unchecked
    /// one would read as inside from anywhere on earth.
    /// </summary>
    public static bool IsValidFix(double latitude, double longitude, double? accuracyMeters) =>
        double.IsFinite(latitude) && double.IsFinite(longitude)
        && latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180
        && (accuracyMeters is null || (double.IsFinite(accuracyMeters.Value) && accuracyMeters.Value >= 0));

    /// <summary>The scan's own rule (AttendanceController: outside when distance &gt; radius), and never inside
    /// on a distance that is not a number.</summary>
    public static bool IsInside(double distanceMeters, int radiusMeters) =>
        double.IsFinite(distanceMeters) && distanceMeters <= radiusMeters;

    public static bool IsLate(DateTime sentAtUtc, DateTime answeredAtUtc) => answeredAtUtc > sentAtUtc + AnswerWindow;

    /// <summary>A result an answer may still replace: still waiting, run out, or a phone that gave no position.
    /// Inside and outside are final — a second answer must not turn «outside» into «inside».</summary>
    public static bool IsAnswerable(PresenceCheckResult result) =>
        result is PresenceCheckResult.Pending or PresenceCheckResult.NoAnswer or PresenceCheckResult.GpsFailed;
}
