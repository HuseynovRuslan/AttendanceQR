using System.Globalization;
using ClosedXML.Excel;

namespace AttendanceQR.Api;

/// <summary>
/// The daily attendance board as a workbook management can actually read.
///
/// This file is sent to the leadership every morning, and it used to be one flat list of every
/// employee in the company from A to Z — Aygün from the laundry between two gardeners from a park
/// eleven kilometres away. Nobody can answer "how is Qala Anbar doing today" from that without
/// sorting it by hand first, so the reader's first act on receiving the report was to rebuild it.
///
/// So the workbook has two sheets, in the order they get read:
///   «Xülasə»    — one line per site with the day's counts, and a CƏMİ line. The whole company on
///                 one screen; this is what somebody looks at before deciding whether to look further.
///   «Davamiyyət» — the same people, GROUPED by site, each group under its own banner carrying that
///                 site's headline figures, and collapsible so a reader can fold away the sites that
///                 are fine.
///
/// The counts are not recomputed here. They arrive already bucketed by the board itself
/// (todayCounts.ts, which has tests) precisely so the file and the screen cannot disagree — the same
/// reason the status TEXT is sent rather than derived: the board learned the hard way that
/// «Ezamiyyət» and «Xəstəlik» reach the client as one status and only the screen knows how to tell
/// them apart. A sheet that re-derives either would be a second place for that bug to live.
/// </summary>
public static class DayBoardSheet
{
    /// <summary>
    /// The buckets the board counts in — <c>countToday</c>'s own names, kept in this order because it
    /// is the order somebody reads a morning: who came, who is still out, who is away.
    ///
    /// The words beside them are only a FALLBACK. The board sends its own labels, because these had
    /// already drifted from it: «Gəlib» here where the screen says «Tamamlayıb», «Gözləmədə» where it
    /// says «Gözlənilir», «Hazırlanır» where it says «Aktivləşdirməyib». A column heading that names
    /// nothing on the screen the file came from is how a reader stops trusting the file.
    /// </summary>
    private static readonly (string Bucket, string Fallback)[] SummaryColumns =
    [
        ("present", "Tamamlayıb"),
        ("incomplete", "İşdə"),
        ("absent", "Qayıb"),
        ("onLeave", "Məzuniyyət"),
        // Ödənişsiz məzuniyyət, counted apart. It was inside «Məzuniyyət» while the dashboard already
        // showed it on its own, so two screens of the same company reported different numbers for one
        // morning — and the detail sheet said «Ödənişsiz məzuniyyət» on the very rows the summary was
        // counting as annual leave.
        ("unpaid", "Ödənişsiz"),
        ("sick", "Xəstəlik"),
        ("trip", "Ezamiyyət"),
        ("permission", "İcazə"),
        // Two different facts, and the second is the one somebody DID: «Həftəlik istirahət» is the
        // roster's own day off, «İstirahət (təyin edilmiş)» is a day a manager granted. Summed into
        // one column, every granted rest day vanished among two hundred ordinary Sundays.
        ("dayOff", "Həftəlik istirahət"),
        ("rest", "İstirahət (təyin edilmiş)"),
        ("pending", "Növbəsi başlamayıb"),
        ("onboarding", "Aktivləşdirməyib"),
    ];

    private static readonly string[] DetailHeaders =
        ["Ad Soyad", "Vəzifə", "Ərazi", "Sənəd üzrə", "Status", "Giriş", "Çıxış", "Şəkil"];

    private static readonly XLColor HeaderBlue = XLColor.FromHtml("#1E70C8");
    private static readonly XLColor GroupBand = XLColor.FromHtml("#DCE9F8");
    private static readonly XLColor TotalBand = XLColor.FromHtml("#FFF4CE");

    /// <summary>
    /// Azerbaijani collation, so «Ə» and «İ» sort where a reader expects instead of after Z. The board
    /// orders itself the same way (sortRows), and a file whose order disagrees with the screen it was
    /// exported from is a file people stop trusting.
    /// </summary>
    private static readonly StringComparer Az = BuildAzComparer();

    private static StringComparer BuildAzComparer()
    {
        try
        {
            return StringComparer.Create(CultureInfo.GetCultureInfo("az"), ignoreCase: true);
        }
        catch (CultureNotFoundException)
        {
            // A container without the Azerbaijani locale data still has to produce a file.
            return StringComparer.OrdinalIgnoreCase;
        }
    }

    public sealed record Row(
        string Name, string Position, string Location, string Status,
        string CheckIn, string CheckOut, string Photo, string? Bucket,
        // «Sənəd üzrə» — the employer the paperwork names when it is not this board's company.
        // Blank on nearly every row; the few that are filled answer the question this file used to
        // raise and could not settle: why a name appears on a company's list that does not employ them.
        string Paper = "");

    /// <param name="labels">
    /// The board's word for each bucket, keyed by bucket name. Anything missing falls back to
    /// <see cref="SummaryColumns"/>. It is the board's vocabulary and not this sheet's on purpose —
    /// including the one label only it can decide, «İşdə» versus «Çıxış yoxdur», which depends on
    /// whether the date being exported has finished.
    /// </param>
    public static byte[] Build(
        string title, IReadOnlyList<Row> rows, string? scopeNote,
        IReadOnlyDictionary<string, string>? labels)
    {
        using var wb = new XLWorkbook();

        var groups = rows
            .GroupBy(r => string.IsNullOrWhiteSpace(r.Location) ? "—" : r.Location.Trim())
            .OrderBy(g => g.Key, Az)
            .Select(g => (Site: g.Key, People: g.OrderBy(r => r.Name, Az).ToList()))
            .ToList();

        string Label(string bucket)
        {
            if (labels is not null && labels.TryGetValue(bucket, out var given) && !string.IsNullOrWhiteSpace(given))
                return given;
            return SummaryColumns.First(c => c.Bucket == bucket).Fallback;
        }

        BuildSummary(wb, title, scopeNote, Label, groups);
        BuildDetail(wb, title, scopeNote, Label, groups);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static int Count(IEnumerable<Row> people, string bucket)
        => people.Count(p => string.Equals(p.Bucket, bucket, StringComparison.Ordinal));

    private static void BuildSummary(
        XLWorkbook wb, string title, string? scopeNote, Func<string, string> label,
        List<(string Site, List<Row> People)> groups)
    {
        var ws = wb.Worksheets.Add("Xülasə");
        var cols = SummaryColumns.Length + 2; // site + headcount + the buckets

        ws.Cell(1, 1).Value = title;
        ws.Range(1, 1, 1, cols).Merge();
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;

        if (!string.IsNullOrWhiteSpace(scopeNote))
        {
            ws.Cell(2, 1).Value = scopeNote;
            ws.Range(2, 1, 2, cols).Merge();
            ws.Cell(2, 1).Style.Font.Italic = true;
            ws.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#555555");
        }

        const int headerRow = 4;
        ws.Cell(headerRow, 1).Value = "Ərazi";
        ws.Cell(headerRow, 2).Value = "İşçi sayı";
        for (var i = 0; i < SummaryColumns.Length; i++)
            ws.Cell(headerRow, i + 3).Value = label(SummaryColumns[i].Bucket);

        var head = ws.Range(headerRow, 1, headerRow, cols);
        head.Style.Font.Bold = true;
        head.Style.Fill.BackgroundColor = HeaderBlue;
        head.Style.Font.FontColor = XLColor.White;
        head.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        head.Style.Alignment.WrapText = true;

        var r = headerRow + 1;
        foreach (var (site, people) in groups)
        {
            ws.Cell(r, 1).Value = site;
            ws.Cell(r, 2).Value = people.Count;
            for (var i = 0; i < SummaryColumns.Length; i++)
                ws.Cell(r, i + 3).Value = Count(people, SummaryColumns[i].Bucket);
            r++;
        }

        // The company line. Summed from the same rows as the lines above it, never from a second
        // source — a total that can disagree with its own table is worse than no total.
        ws.Cell(r, 1).Value = "CƏMİ";
        ws.Cell(r, 2).Value = groups.Sum(g => g.People.Count);
        for (var i = 0; i < SummaryColumns.Length; i++)
            ws.Cell(r, i + 3).Value = groups.Sum(g => Count(g.People, SummaryColumns[i].Bucket));

        var total = ws.Range(r, 1, r, cols);
        total.Style.Font.Bold = true;
        total.Style.Fill.BackgroundColor = TotalBand;

        var table = ws.Range(headerRow, 1, r, cols);
        table.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        table.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

        ws.Column(1).Width = 30;
        for (var c = 2; c <= cols; c++)
            ws.Column(c).Width = 11;
        ws.SheetView.FreezeRows(headerRow);
    }

    private static void BuildDetail(
        XLWorkbook wb, string title, string? scopeNote, Func<string, string> label,
        List<(string Site, List<Row> People)> groups)
    {
        var ws = wb.Worksheets.Add("Davamiyyət");
        var cols = DetailHeaders.Length;

        ws.Cell(1, 1).Value = title;
        ws.Range(1, 1, 1, cols).Merge();
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;

        if (!string.IsNullOrWhiteSpace(scopeNote))
        {
            ws.Cell(2, 1).Value = scopeNote;
            ws.Range(2, 1, 2, cols).Merge();
            ws.Cell(2, 1).Style.Font.Italic = true;
            ws.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#555555");
        }

        const int headerRow = 4;
        for (var i = 0; i < cols; i++)
            ws.Cell(headerRow, i + 1).Value = DetailHeaders[i];

        var head = ws.Range(headerRow, 1, headerRow, cols);
        head.Style.Font.Bold = true;
        head.Style.Fill.BackgroundColor = HeaderBlue;
        head.Style.Font.FontColor = XLColor.White;
        head.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        var r = headerRow + 1;
        foreach (var (site, people) in groups)
        {
            // The site's banner carries its own headline figures, so a reader folding through the
            // sheet never has to go back to the summary to know whether this group needs attention.
            ws.Cell(r, 1).Value = $"{site}  —  {people.Count} nəfər{Headline(people, label)}";
            ws.Range(r, 1, r, cols).Merge();
            var band = ws.Range(r, 1, r, cols);
            band.Style.Font.Bold = true;
            band.Style.Fill.BackgroundColor = GroupBand;
            band.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            r++;

            var first = r;
            foreach (var p in people)
            {
                ws.Cell(r, 1).Value = p.Name;
                ws.Cell(r, 2).Value = p.Position;
                ws.Cell(r, 3).Value = p.Location;
                ws.Cell(r, 4).Value = p.Paper;
                ws.Cell(r, 5).Value = p.Status;
                ws.Cell(r, 6).Value = p.CheckIn;
                ws.Cell(r, 7).Value = p.CheckOut;
                ws.Cell(r, 8).Value = p.Photo;
                r++;
            }

            if (r > first)
            {
                var body = ws.Range(first, 1, r - 1, cols);
                body.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                body.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                // Collapsible: a site that is fine folds away, which is the only way a 500-person
                // company's board fits on a screen.
                ws.Rows(first, r - 1).Group();
            }
        }

        ws.Column(1).Width = 30;
        ws.Column(2).Width = 20;
        ws.Column(3).Width = 24;
        ws.Column(4).Width = 16;
        ws.Column(5).Width = 11;
        ws.Column(6).Width = 11;
        ws.Column(7).Width = 9;
        ws.SheetView.FreezeRows(headerRow);
    }

    /// <summary>
    /// «· Gəlib 8 · Qayıb 1» — only the buckets that are not empty, so the banner stays short and
    /// every number on it means something.
    ///
    /// The labels are printed as they are written, never lower-cased. Azerbaijani has a dotted capital
    /// «İ» that no invariant lower-casing maps to «i», so «İcazə» came out capitalised while every
    /// word beside it did not — and the culture-aware alternative is the Turkish-I trap, which is a
    /// worse thing to invite into a report for the sake of a letter case.
    /// </summary>
    private static string Headline(List<Row> people, Func<string, string> label)
    {
        var parts = SummaryColumns
            .Select(c => (Label: label(c.Bucket), Count: Count(people, c.Bucket)))
            .Where(x => x.Count > 0)
            .Select(x => $"{x.Label} {x.Count}");
        var text = string.Join(" · ", parts);
        return text.Length == 0 ? string.Empty : $"  ·  {text}";
    }
}
