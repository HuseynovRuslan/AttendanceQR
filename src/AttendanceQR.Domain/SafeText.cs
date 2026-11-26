namespace AttendanceQR.Domain;

/// <summary>
/// Cutting somebody's text to a length without breaking it.
///
/// A C# string is UTF-16, and an emoji (anything outside the Basic Multilingual Plane) is two units of it: a high
/// surrogate and a low one. <c>text[..200]</c> counts units, so when the 200th unit is the first half of an emoji the
/// result ends in half a character. Npgsql writes text with a strict UTF-8 encoder, which cannot encode half a
/// character and throws before anything reaches the database — so the save fails as a 500, the error is in no
/// Postgres log, and the in-memory tests, which store any string, never see it. A reason typed with a «👍» in the
/// wrong place was enough.
///
/// Every place that shortens what a person typed before saving it goes through <see cref="Truncate"/>.
/// </summary>
public static class SafeText
{
    /// <summary>
    /// <paramref name="text"/> cut to at most <paramref name="max"/> UTF-16 units, never between the two halves of a
    /// surrogate pair: when the cut would fall there, the whole character is left out instead.
    /// </summary>
    public static string Truncate(string text, int max)
    {
        if (text.Length <= max)
            return text;
        var cut = Math.Max(max, 0);
        if (cut > 0 && char.IsHighSurrogate(text[cut - 1]))
            cut--;
        return text[..cut];
    }
}
