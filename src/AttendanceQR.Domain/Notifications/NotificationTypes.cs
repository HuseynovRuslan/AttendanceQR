namespace AttendanceQR.Domain.Notifications;

/// <summary>
/// The catalogue of notification types — the one place a type is declared, the way <c>AccessPermissions</c> declares
/// permissions. A type belongs to a category; a person's settings (<see cref="Entities.NotificationPreference"/>) are kept
/// per category, so a later module (leave requests, approvals, reports) adds its category and types here and reuses
/// the whole pipeline: the outbox, the dispatcher, the center, the push and the live channel.
///
/// Every type is OFF until the person switches it on. Nothing here is sent to anybody who did not ask for it.
/// </summary>
public static class NotificationTypes
{
    /// <summary>Attendance of the people one may see: check-ins, check-outs, late arrivals.</summary>
    public const string CategoryAttendance = "attendance";

    public const string CheckIn = "attendance.check_in";
    public const string CheckOut = "attendance.check_out";

    /// <summary>
    /// Judged against the person's OWN shift for that day (plan, rotation, personal hours) and its grace — never the
    /// branch's general hours, which is why the old branch-wide «Gecikmə» display was removed. Never for a flexible
    /// schedule, a holiday, a leave day, a rest day or a second stretch of the day.
    /// </summary>
    public const string Late = "attendance.late";

    public sealed record Definition(string Key, string Category, string Label, string Description);

    public static readonly IReadOnlyList<Definition> All =
    [
        new(CheckIn, CategoryAttendance, "İşə giriş", "İşçi giriş etdikdə bildiriş alın."),
        new(CheckOut, CategoryAttendance, "İşdən çıxış", "İşçi çıxış etdikdə bildiriş alın."),
        new(Late, CategoryAttendance, "Gecikmə", "İşçi işə gecikdikdə bildiriş alın."),
    ];

    public static readonly IReadOnlyList<string> Categories = [CategoryAttendance];

    public static Definition? Find(string key) => All.FirstOrDefault(d => d.Key == key);

    public static IEnumerable<Definition> InCategory(string category) => All.Where(d => d.Category == category);

    /// <summary>The keys a stored list names that are real types of <paramref name="category"/>, each once, in catalogue order.</summary>
    public static IReadOnlyList<string> Normalize(string category, IEnumerable<string>? keys)
    {
        var wanted = (keys ?? []).Select(k => k?.Trim()).Where(k => !string.IsNullOrEmpty(k)).ToHashSet(StringComparer.Ordinal);
        return InCategory(category).Select(d => d.Key).Where(wanted.Contains).ToList();
    }

    /// <summary>CSV ↔ list, the way <c>Tenant.DisabledFeatures</c> stores its keys.</summary>
    public static IReadOnlyList<string> ParseCsv(string? csv) =>
        string.IsNullOrWhiteSpace(csv) ? [] : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string ToCsv(IEnumerable<string> keys) => string.Join(',', keys);
}

/// <summary>What happened, as the outbox records it (<see cref="Entities.NotificationEvent.Kind"/>).</summary>
public static class NotificationEventKinds
{
    /// <summary>A check-in was committed. The dispatcher derives both <see cref="NotificationTypes.CheckIn"/> and, when
    /// the arrival was late by the person's own shift, <see cref="NotificationTypes.Late"/> from it.</summary>
    public const string CheckIn = "attendance.check_in";
    public const string CheckOut = "attendance.check_out";
}

public enum NotificationBranchMode
{
    /// <summary>Every branch the person may see, now and later — a branch added to their scope is included.</summary>
    AllPermitted = 0,
    /// <summary>Only the branches they picked (<see cref="Entities.NotificationBranchPreference"/>), and only while they may still see them.</summary>
    Selected = 1,
}

public enum NotificationChannel
{
    Push = 1,
    Web = 2,
}

public enum NotificationDeliveryStatus
{
    Pending = 0,
    Sent = 1,
    /// <summary>Nobody's device was subscribed — not an error, not retried.</summary>
    NoDevice = 2,
    /// <summary>Gave up after the retries.</summary>
    Failed = 3,
    /// <summary>Not sent on purpose: push not configured on this server.</summary>
    Skipped = 4,
    /// <summary>Folded into one summary push with others for the same person (a burst at shift start).</summary>
    Digested = 5,
}

public enum NotificationEventStatus
{
    Pending = 0,
    Done = 1,
    Failed = 2,
}
