using System.Globalization;

namespace AttendanceQR.Application.Reporting;

/// <summary>
/// Hours that differ by day of the week, on top of a shift's ordinary ones.
///
/// A shift has had exactly one start and one end since the beginning, and that is genuinely all most
/// of them need. But a real one at Heydər Əliyev Mərkəzi is 08:00–18:00 from Monday to Friday and
/// 09:00–18:00 at the weekend, and there was no way to say it: a schedule holds one pair of times,
/// and an employee holds one schedule.
///
/// Stored as a short string on the schedule rather than a child table, deliberately. Every place that
/// judges a day already loads the whole <see cref="Domain.Entities.Schedule"/> row — six of them, via
/// <c>Schedules.ToDictionaryAsync</c> — so a column arrives everywhere for free, while a table would
/// be a join each of those six had to remember. Forgetting one would not fail: it would silently
/// judge somebody's day against the wrong hours, in the code that decides pay.
///
/// Format: <c>6=09:00-18:00;0=09:00-18:00</c> — day number (Sunday=0 … Saturday=6, the same layout
/// WorkDaysMask uses) then the hours. Only the days that DIFFER appear; everything else falls through
/// to the shift's own times.
///
/// Parsing never throws. A malformed string means the day falls back to the shift's ordinary hours,
/// because the alternative — an exception from a column somebody hand-edited — is the nightly summary
/// job dying and a whole company's attendance going unrecorded.
/// </summary>
public static class DayHours
{
    /// <summary>An empty map, shared — the overwhelmingly common case is no overrides at all.</summary>
    public static readonly IReadOnlyDictionary<DayOfWeek, (TimeOnly Start, TimeOnly End)> None
        = new Dictionary<DayOfWeek, (TimeOnly, TimeOnly)>();

    public static IReadOnlyDictionary<DayOfWeek, (TimeOnly Start, TimeOnly End)> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return None;

        var map = new Dictionary<DayOfWeek, (TimeOnly, TimeOnly)>();
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;

            if (!int.TryParse(part[..eq], NumberStyles.Integer, CultureInfo.InvariantCulture, out var day)) continue;
            if (day is < 0 or > 6) continue;

            var dash = part.IndexOf('-', eq);
            if (dash < 0) continue;

            if (!TimeOnly.TryParseExact(part[(eq + 1)..dash].Trim(), "HH\\:mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var start)) continue;
            if (!TimeOnly.TryParseExact(part[(dash + 1)..].Trim(), "HH\\:mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var end)) continue;

            // Last one wins if a day is listed twice — an arbitrary but stable choice, so the same
            // string always resolves the same way.
            map[(DayOfWeek)day] = (start, end);
        }

        return map.Count == 0 ? None : map;
    }

    /// <summary>Back to the stored form, days in order so the column does not churn on every save.</summary>
    public static string? Format(IReadOnlyDictionary<DayOfWeek, (TimeOnly Start, TimeOnly End)>? map)
    {
        if (map is null || map.Count == 0) return null;

        return string.Join(';', map
            .OrderBy(kv => (int)kv.Key)
            .Select(kv => $"{(int)kv.Key}={kv.Value.Start:HH\\:mm}-{kv.Value.End:HH\\:mm}"));
    }
}
