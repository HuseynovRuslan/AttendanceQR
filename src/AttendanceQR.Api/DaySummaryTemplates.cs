namespace AttendanceQR.Api;

/// <summary>
/// A company's own form for the «Xülasə» sheet of the daily export: its areas, in its order, under
/// the headings of the paper list it already keeps.
///
/// Opt-in, per export. A template is offered in the export dialog only to the company it belongs to,
/// and only used when the person exporting chooses it — never because the areas happen to fit. Many of
/// the company's branch managers have every one of their branches inside the 27-line list; matching on
/// that alone would have turned their few-branch summaries into a 27-line form with most of its lines
/// empty, on a morning nobody asked for it.
///
/// A line is filled from the rows of the export whose area has its name; a line no row names stays
/// blank — never 0, because 0 is a count and these are lines with nothing behind them (an area the
/// company has not opened as a branch yet, or one not chosen for this file). An area in the export
/// that has NO line here is refused rather than dropped: a summary that silently loses an area is the
/// one failure this sheet cannot afford. See <see cref="Unlisted"/>.
/// </summary>
/// <param name="Key">Stable identifier the client sends back when it chooses this template.</param>
/// <param name="Name">What the export dialog calls it.</param>
/// <param name="Areas">The lines, top to bottom, spelled as the company's own list spells them.</param>
public sealed record DaySummaryTemplate(string Key, string Name, IReadOnlyList<string> Areas)
{
    /// <summary>The areas among <paramref name="areas"/> that have no line of their own, each once,
    /// in the order first met. Empty means the export fits the template.</summary>
    public IReadOnlyList<string> Unlisted(IEnumerable<string> areas)
    {
        var unlisted = new List<string>();
        foreach (var area in areas.Select(DayBoardSheet.AreaOf))
        {
            if (LineOf(area) < 0 && !unlisted.Any(u => DayBoardSheet.SameArea(u, area)))
                unlisted.Add(area);
        }
        return unlisted;
    }

    /// <summary>The 0-based line an area is printed on, or -1 when it has none.</summary>
    public int LineOf(string area)
    {
        for (var i = 0; i < Areas.Count; i++)
        {
            if (DayBoardSheet.SameArea(Areas[i], area))
                return i;
        }
        return -1;
    }
}

/// <summary>
/// Which company has which <see cref="DaySummaryTemplate"/>. Kept in code, keyed by the company's id:
/// a list a reviewer can read and a test can pin, and that no other company can ever be shown.
/// </summary>
public static class DaySummaryTemplates
{
    public const string DavamiyyetSiyahisi = "davamiyyet-siyahisi";

    // Bakı Abadlıq Xidməti (baki-abadliq-xidmeti).
    private static readonly Guid BakiAbadliq = Guid.Parse("a0f02283-52be-4aef-acad-c89de109ee52");

    /// <summary>
    /// «Şablon Davamiyyət siyahısı» (the owner, 2026-10-21) — the 24 branches of one operator's area in
    /// the order the list keeps them, with three lines the company counts on paper but has not opened as
    /// branches here yet: Zoopark, Texniki Şöbə, Bakı Əlavə Qüvvə. Those three stay blank until a branch
    /// of that name exists and is in the export; nothing is borrowed from another company or invented.
    /// </summary>
    public static readonly DaySummaryTemplate BakiAbadliqList = new(
        DavamiyyetSiyahisi,
        "Davamiyyət siyahısı (şablon)",
        [
            "Nərimanov Ofis",
            "Heydər Əliyev Mərkəzi",
            "Fəvvarələr Meydanı",
            "Bakı Konqres Mərkəzi",
            "Dədə Qorqud Parkı",
            "Novxanı",
            "Bibiheybət",
            "Qala Anbar",
            "Qafur Məmmədov Parkı",
            "Zoopark",
            "Texniki Şöbə",
            "Bakı Əlavə Qüvvə",
            "ADNSU",
            "İDEA",
            "Bayıl yolu",
            "Ümumi Ərazilər",
            "Zabitlər parkı",
            "Xətai parkı",
            "Nərimanov heykəli",
            "Puşkin parkı",
            "Bülbül parkı",
            "Milli konservatoriya",
            "Muzey mərkəzi",
            "Parlament parkı",
            "Ümumi parklar",
            "H.Əliyev adına park Badamdar",
            "Axundov parkı",
        ]);

    /// <summary>The templates <paramref name="tenantId"/> may use — none for almost every company.</summary>
    public static IReadOnlyList<DaySummaryTemplate> For(Guid tenantId)
        => tenantId == BakiAbadliq ? [BakiAbadliqList] : [];

    public static DaySummaryTemplate? Find(Guid tenantId, string? key)
        => string.IsNullOrWhiteSpace(key) ? null : For(tenantId).FirstOrDefault(t => t.Key == key);
}
