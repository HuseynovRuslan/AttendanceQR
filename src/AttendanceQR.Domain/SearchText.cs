using System.Text;

namespace AttendanceQR.Domain;

/// <summary>
/// The one rule for matching what somebody typed into a search box against a name — in C# and, through the
/// mapping in AppDbContext, in SQL, so the database and the application cannot disagree about it.
///
/// Why a rule of our own. The admin's staff search lowered the search text in .NET and the name in Postgres,
/// and the two do not agree about Azerbaijani: .NET's invariant casing leaves «İ» and «ı» alone (they are the
/// Turkic letters it refuses to guess about), Postgres turns «İ» into 'i'. So «İsmayılov» typed with its
/// capital found nobody — not even «İsmayılov» (staging E2E, 2026-10-17).
///
/// What it does:
///  - Azerbaijani case pairs are folded by hand, not by locale: İ→i, I→ı, Ə→ə, Ş→ş, Ç→ç, Ğ→ğ, Ö→ö, Ü→ü. The
///    database's own lower() then only has to handle the letters every locale agrees on.
///  - The dotted and the dotless i are one letter for searching: «İ», «I», «ı» and «i» all read as 'i'. People
///    type names on keyboards that have no «ı» or «İ» — «Ismayilov», «ISMAYILOV» — and a search that misses
///    a colleague because of a dot is worse than one that also offers a namesake who differs by a dot. This is
///    a search rule only; matching a name to an identity (PersonName.Fold) stays strict.
///  - A stray COMBINING DOT ABOVE (what some tools write after an «i») is dropped.
/// </summary>
public static class SearchText
{
    /// <summary>Folded by hand, in order: <see cref="From"/>[k] becomes <see cref="To"/>[k]; letters of
    /// <see cref="From"/> past the end of <see cref="To"/> are deleted (Postgres translate() semantics).</summary>
    public const string From = "İIıƏŞÇĞÖÜ̇";

    /// <inheritdoc cref="From"/>
    public const string To = "iiiəşçğöü";

    /// <summary>The search key of <paramref name="text"/>. Same answer as SQL lower(translate(text, From, To)).</summary>
    public static string Fold(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            var k = From.IndexOf(c);
            if (k < 0)
                sb.Append(char.ToLowerInvariant(c));
            else if (k < To.Length)
                sb.Append(To[k]);
        }
        return sb.ToString();
    }
}
