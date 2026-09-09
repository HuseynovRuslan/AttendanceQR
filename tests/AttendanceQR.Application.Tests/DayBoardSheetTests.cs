using AttendanceQR.Api;
using ClosedXML.Excel;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The workbook the leadership receives every morning.
///
/// It was one flat A-to-Z list of the whole company: a laundry worker between two gardeners from a
/// park eleven kilometres away, and no way to answer "how is Qala Anbar today" without sorting it by
/// hand first. The owner's words for it were that it comes out «a-dan z», not structured.
///
/// What is pinned here is the structure — a summary line per site, the detail grouped under site
/// banners — and the one property that matters more than any of it: the numbers on the summary are
/// the BOARD's numbers. They are counted from the buckets the screen sends, never re-derived, because
/// «Ezamiyyət» and «Xəstəlik» reach the client as one status and a sheet that guessed would repeat a
/// bug this product has already shipped twice.
/// </summary>
public class DayBoardSheetTests
{
    /// <summary>The board's own labels, as the screen sends them — the file must speak its words,
    /// not its own. «Tamamlayıb» rather than «Gəlib» is the one this product retired on purpose.</summary>
    private static readonly Dictionary<string, string> BoardWords = new()
    {
        ["present"] = "Tamamlayıb", ["incomplete"] = "İşdə", ["absent"] = "Qayıb",
        ["onLeave"] = "Məzuniyyət", ["sick"] = "Xəstəlik", ["trip"] = "Ezamiyyət",
        ["permission"] = "İcazə", ["dayOff"] = "İstirahət",
        ["pending"] = "Növbəsi başlamayıb", ["onboarding"] = "Aktivləşdirməyib",
    };

    private static DayBoardSheet.Row Person(
        string name, string site, string bucket, string status = "", string position = "")
        => new(name, position, site, status, "09:00", "18:00", "var", bucket);

    private static XLWorkbook Build(params DayBoardSheet.Row[] rows)
    {
        var bytes = DayBoardSheet.Build(
            "Davamiyyət — 7 sentyabr", rows, "3 ərazi seçilib", BoardWords);
        return new XLWorkbook(new MemoryStream(bytes));
    }

    /// <summary>Finds a row on a sheet by the text in its first column.</summary>
    private static IXLRow? RowStarting(IXLWorksheet ws, string text)
        => ws.RowsUsed().FirstOrDefault(r => r.Cell(1).GetString().StartsWith(text, StringComparison.Ordinal));

    [Fact]
    public void The_summary_comes_first_because_that_is_what_gets_read_first()
    {
        using var wb = Build(Person("Aygün", "Qala Anbar", "present"));

        Assert.Equal("Xülasə", wb.Worksheet(1).Name);
        Assert.Equal("Davamiyyət", wb.Worksheet(2).Name);
    }

    [Fact]
    public void Every_site_gets_one_summary_line_and_the_counts_are_the_boards_own()
    {
        using var wb = Build(
            Person("Aygün", "Qala Anbar", "present"),
            Person("Bəhruz", "Qala Anbar", "present"),
            Person("Cavid", "Qala Anbar", "absent"),
            Person("Dilarə", "Camaşırxana", "trip"),
            Person("Elçin", "Camaşırxana", "sick"));

        var ws = wb.Worksheet("Xülasə");
        var qala = RowStarting(ws, "Qala Anbar")!;
        var lacin = RowStarting(ws, "Camaşırxana")!;

        Assert.Equal(3, qala.Cell(2).GetValue<int>());   // headcount
        Assert.Equal(2, qala.Cell(3).GetValue<int>());   // Gəlib
        Assert.Equal(1, qala.Cell(5).GetValue<int>());   // Qayıb
        Assert.Equal(2, lacin.Cell(2).GetValue<int>());

        // The distinction the board exists to keep: a work trip is NOT annual leave, and sick is
        // neither. All three arrive as one status and are separable only by the bucket.
        var headers = ws.Row(4);
        var trip = Enumerable.Range(1, 12).First(c => headers.Cell(c).GetString() == "Ezamiyyət");
        var sick = Enumerable.Range(1, 12).First(c => headers.Cell(c).GetString() == "Xəstəlik");
        var leave = Enumerable.Range(1, 12).First(c => headers.Cell(c).GetString() == "Məzuniyyət");
        Assert.Equal(1, lacin.Cell(trip).GetValue<int>());
        Assert.Equal(1, lacin.Cell(sick).GetValue<int>());
        Assert.Equal(0, lacin.Cell(leave).GetValue<int>());
    }

    [Fact]
    public void The_company_line_is_summed_from_the_same_rows_it_sits_under()
    {
        using var wb = Build(
            Person("Aygün", "Qala Anbar", "present"),
            Person("Bəhruz", "Camaşırxana", "present"),
            Person("Cavid", "Camaşırxana", "absent"));

        var ws = wb.Worksheet("Xülasə");
        var total = RowStarting(ws, "CƏMİ")!;

        Assert.Equal(3, total.Cell(2).GetValue<int>());
        Assert.Equal(2, total.Cell(3).GetValue<int>());  // Gəlib
        Assert.Equal(1, total.Cell(5).GetValue<int>());  // Qayıb
    }

    [Fact]
    public void The_detail_sheet_puts_each_site_under_its_own_banner()
    {
        using var wb = Build(
            Person("Zaur", "Qala Anbar", "present"),
            Person("Aygün", "Qala Anbar", "absent"),
            Person("Bəhruz", "Camaşırxana", "present"));

        var ws = wb.Worksheet("Davamiyyət");
        var banner = RowStarting(ws, "Qala Anbar")!;

        // The banner says how many and how they are doing, so a reader folding through the sheet
        // never has to go back to the summary.
        Assert.Contains("2 nəfər", banner.Cell(1).GetString());
        Assert.Contains("Tamamlayıb 1", banner.Cell(1).GetString());
        Assert.Contains("Qayıb 1", banner.Cell(1).GetString());

        // Its people follow it, alphabetically, and nobody else's do.
        Assert.Equal("Aygün", ws.Row(banner.RowNumber() + 1).Cell(1).GetString());
        Assert.Equal("Zaur", ws.Row(banner.RowNumber() + 2).Cell(1).GetString());
    }

    [Fact]
    public void Sites_and_people_sort_the_way_an_Azerbaijani_reader_expects()
    {
        // «Ə» belongs after E, not after Z — which is where an ordinal sort puts it, and where the
        // flat export used to leave a third of the company's names.
        using var wb = Build(
            Person("Zaur", "Zavod", "present"),
            Person("Əli", "Zavod", "present"),
            Person("Elçin", "Zavod", "present"));

        var ws = wb.Worksheet("Davamiyyət");
        var banner = RowStarting(ws, "Zavod")!;
        var order = new[]
        {
            ws.Row(banner.RowNumber() + 1).Cell(1).GetString(),
            ws.Row(banner.RowNumber() + 2).Cell(1).GetString(),
            ws.Row(banner.RowNumber() + 3).Cell(1).GetString(),
        };

        Assert.Equal(new[] { "Elçin", "Əli", "Zaur" }, order);
    }

    [Fact]
    public void A_row_from_an_older_client_is_counted_as_a_head_and_never_guessed_at()
    {
        // No bucket means the client did not say which column this person belongs in. They are a
        // person and are counted as one; inventing a column for them would be a number that lies.
        using var wb = Build(new DayBoardSheet.Row("Aygün", "", "Qala Anbar", "Gəlib", "09:00", "", "yox", null));

        var ws = wb.Worksheet("Xülasə");
        var site = RowStarting(ws, "Qala Anbar")!;

        Assert.Equal(1, site.Cell(2).GetValue<int>());
        for (var c = 3; c <= 12; c++)
            Assert.Equal(0, site.Cell(c).GetValue<int>());
    }

    [Fact]
    public void The_incomplete_column_is_named_by_the_board_not_by_this_sheet()
    {
        // «İşdə» while the day is still running, «Çıxış yoxdur» once it is over. Only the board knows
        // which date it is exporting, so it says, and the sheet obeys.
        var closed = new Dictionary<string, string>(BoardWords) { ["incomplete"] = "Çıxış yoxdur" };
        var bytes = DayBoardSheet.Build("D", [Person("A", "S", "incomplete")], null, closed);
        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var header = wb.Worksheet("Xülasə").Row(4);

        Assert.Contains(
            Enumerable.Range(1, header.LastCellUsed()!.Address.ColumnNumber).Select(c => header.Cell(c).GetString()),
            h => h == "Çıxış yoxdur");
    }

    [Fact]
    public void The_sheet_speaks_the_boards_words_not_its_own()
    {
        // The three that had drifted. «Gəlib» in particular is a word this product retired: it read
        // as though somebody still at work had not come.
        using var wb = Build(
            Person("A", "S", "present"), Person("B", "S", "pending"), Person("C", "S", "onboarding"));
        var header = wb.Worksheet("Xülasə").Row(4);
        var words = Enumerable.Range(1, header.LastCellUsed()!.Address.ColumnNumber).Select(c => header.Cell(c).GetString()).ToList();

        Assert.Contains("Tamamlayıb", words);
        Assert.Contains("Növbəsi başlamayıb", words);
        Assert.Contains("Aktivləşdirməyib", words);
        Assert.DoesNotContain("Gəlib", words);
    }

    [Fact]
    public void The_file_says_what_it_covers()
    {
        // A report that was exported for three sites out of twenty must never look like the company.
        var bytes = DayBoardSheet.Build("D", [Person("A", "S", "present")], "3 ərazi: S, T, U", BoardWords);
        using var wb = new XLWorkbook(new MemoryStream(bytes));

        Assert.Equal("3 ərazi: S, T, U", wb.Worksheet("Xülasə").Cell(2, 1).GetString());
        Assert.Equal("3 ərazi: S, T, U", wb.Worksheet("Davamiyyət").Cell(2, 1).GetString());
    }

    [Fact]
    public void An_empty_day_still_produces_a_readable_file()
    {
        using var wb = Build();

        Assert.Equal(2, wb.Worksheets.Count);
        Assert.Equal(0, RowStarting(wb.Worksheet("Xülasə"), "CƏMİ")!.Cell(2).GetValue<int>());
    }

    [Fact]
    public void The_detail_sheet_names_the_employer_the_paperwork_gives()
    {
        // The morning workbook kept raising a question it could not settle — why a name appears on a
        // company's list that does not employ them. Blank on nearly every row, and deliberately blank
        // rather than a dash, so the eye lands on the few that are filled.
        var bytes = DayBoardSheet.Build(
            "Davamiyyət",
            [
                new DayBoardSheet.Row("Çingiz Hümbətov", "Ofis Meneceri", "Green Garden", "Tamamlayıb",
                    "08:02", "18:01", "", "present", "Bakı Abadlıq Xidməti / Nərimanov Ofis"),
                new DayBoardSheet.Row("Öz adamı", "Bağban", "Green Garden", "Tamamlayıb",
                    "08:00", "18:00", "", "present"),
            ],
            null, null);

        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet("Davamiyyət");

        var header = ws.RowsUsed().First(r => r.Cell(1).GetString() == "Ad Soyad");
        var paper = Enumerable.Range(1, 10).First(c => header.Cell(c).GetString() == "Sənəd üzrə");
        var status = Enumerable.Range(1, 10).First(c => header.Cell(c).GetString() == "Status");

        var borrowed = ws.RowsUsed().First(r => r.Cell(1).GetString() == "Çingiz Hümbətov");
        var own = ws.RowsUsed().First(r => r.Cell(1).GetString() == "Öz adamı");

        Assert.Equal("Bakı Abadlıq Xidməti / Nərimanov Ofis", borrowed.Cell(paper).GetString());
        Assert.Equal(string.Empty, own.Cell(paper).GetString());
        // The column was inserted BEFORE Status; every cell after it had to move with its header.
        Assert.Equal("Tamamlayıb", borrowed.Cell(status).GetString());
    }
}
