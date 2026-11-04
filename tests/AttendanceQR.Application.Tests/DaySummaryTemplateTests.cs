using AttendanceQR.Api;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Infrastructure.Multitenancy;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// «Xülasə» in Bakı Abadlıq's own form — the «Şablon Davamiyyət siyahısı» the owner sent on 2026-10-21.
///
/// What is pinned: the headings and their order word for word; the 27 lines in the list's order,
/// 24 branches and three lines with no branch behind them yet; the counts coming from the board's own
/// buckets; a CƏMİ that adds up the lines that have people and nothing else; and the two refusals
/// that keep the form honest — an area with no line is never dropped, and a line with nobody is never
/// written as 0. The form exists for one company only and only when somebody chooses it.
/// </summary>
public class DaySummaryTemplateTests
{
    private static readonly Guid BakiAbadliq = Guid.Parse("a0f02283-52be-4aef-acad-c89de109ee52");

    private static readonly string[] Headings =
    [
        "№", "Ərazi", "İşdə", "İstirahət (təyin edilmiş)", "Həftəlik istirahət", "Məzuniyyət", "Qayıb",
        "İcazə", "Xəstəlik", "Ezamiyyət", "Ödənişsiz", "Növbəsi başlamayıb", "İşçi sayı", "Tamamlayıb",
        "Aktivləşdirməyib",
    ];

    // The owner's list, line for line. Lines 10–12 have no branch in QRLog yet.
    private static readonly string[] Lines =
    [
        "Nərimanov Ofis", "Heydər Əliyev Mərkəzi", "Fəvvarələr Meydanı", "Bakı Konqres Mərkəzi",
        "Dədə Qorqud Parkı", "Novxanı", "Bibiheybət", "Qala Anbar", "Qafur Məmmədov Parkı",
        "Zoopark", "Texniki Şöbə", "Bakı Əlavə Qüvvə",
        "ADNSU", "İDEA", "Bayıl yolu", "Ümumi Ərazilər", "Zabitlər parkı", "Xətai parkı",
        "Nərimanov heykəli", "Puşkin parkı", "Bülbül parkı", "Milli konservatoriya", "Muzey mərkəzi",
        "Parlament parkı", "Ümumi parklar", "H.Əliyev adına park Badamdar", "Axundov parkı",
    ];

    private static readonly string[] NotYetBranches = ["Zoopark", "Texniki Şöbə", "Bakı Əlavə Qüvvə"];

    private static readonly string[] Branches = Lines.Except(NotYetBranches).ToArray();

    private static DaySummaryTemplate Form => DaySummaryTemplates.BakiAbadliqList;

    // The board's words — including the end-of-day one, which the form must NOT print.
    private static readonly Dictionary<string, string> BoardWords = new()
    {
        ["present"] = "Tamamlayıb", ["incomplete"] = "Çıxış yoxdur", ["absent"] = "Qayıb",
        ["onLeave"] = "Məzuniyyət", ["unpaid"] = "Ödənişsiz", ["sick"] = "Xəstəlik", ["trip"] = "Ezamiyyət",
        ["permission"] = "İcazə", ["dayOff"] = "Həftəlik istirahət", ["rest"] = "İstirahət (təyin edilmiş)",
        ["pending"] = "Növbəsi başlamayıb", ["onboarding"] = "Aktivləşdirməyib",
    };

    private static int _seq;

    private static DayBoardSheet.Row Person(string site, string bucket)
        => new($"İşçi {Interlocked.Increment(ref _seq)}", "Bağban", site, "status", "09:00", "", "var", bucket);

    private static IEnumerable<DayBoardSheet.Row> People(string site, string bucket, int n)
        => Enumerable.Range(0, n).Select(_ => Person(site, bucket));

    private static XLWorkbook Build(IEnumerable<DayBoardSheet.Row> rows, DaySummaryTemplate? template)
        => new(new MemoryStream(DayBoardSheet.Build(
            "Davamiyyət — 21 oktyabr 2026, çərşənbə", rows.ToList(), "Faktiki ərazi · 24 ərazi", BoardWords, template)));

    /// <summary>Everybody at every branch of the list, one present each — the whole division.</summary>
    private static List<DayBoardSheet.Row> OnePerBranch() => Branches.Select(b => Person(b, "present")).ToList();

    private static int LineRow(string area) => 3 + Array.IndexOf(Lines, area);

    private static int Col(string heading) => Array.IndexOf(Headings, heading) + 1;

    [Fact]
    public void The_headings_are_the_lists_word_for_word_in_the_lists_order_on_row_2()
    {
        using var wb = Build(OnePerBranch(), Form);
        var ws = wb.Worksheet("Xülasə");

        Assert.Equal(Headings, Enumerable.Range(1, 15).Select(c => ws.Cell(2, c).GetString()));
        Assert.True(ws.Cell(2, 16).IsEmpty());
        // Row 1 is the list's empty row — the headings do not move down for a title.
        Assert.True(ws.Row(1).IsEmpty());
        // «İşdə» is the list's word whatever the day; the board would have sent «Çıxış yoxdur».
        Assert.Equal("İşdə", ws.Cell(2, 3).GetString());
        Assert.Equal("İşçi sayı", ws.Cell(2, 13).GetString());
    }

    [Fact]
    public void All_27_lines_are_printed_numbered_in_the_lists_order_and_CEMI_comes_last()
    {
        using var wb = Build(OnePerBranch(), Form);
        var ws = wb.Worksheet("Xülasə");

        for (var i = 0; i < Lines.Length; i++)
        {
            Assert.Equal(i + 1, ws.Cell(3 + i, 1).GetValue<int>());
            Assert.Equal(Lines[i], ws.Cell(3 + i, 2).GetString());
        }
        Assert.Equal("CƏMİ", ws.Cell(30, 1).GetString());
        Assert.Contains(ws.MergedRanges, m => m.RangeAddress.ToString() == "A30:B30");
        Assert.Equal(30, ws.LastRowUsed()!.RowNumber());
    }

    [Fact]
    public void A_line_with_no_branch_behind_it_stays_blank_never_zero()
    {
        using var wb = Build(OnePerBranch(), Form);
        var ws = wb.Worksheet("Xülasə");

        foreach (var line in NotYetBranches)
        {
            var row = LineRow(line);
            Assert.Equal(line, ws.Cell(row, 2).GetString());
            for (var c = 3; c <= 15; c++)
                Assert.True(ws.Cell(row, c).IsEmpty(), $"{line}: column {c} must be blank");
        }
        // And every branch line carries numbers — zeros included, because there a 0 is a count.
        foreach (var branch in Branches)
        {
            var row = LineRow(branch);
            for (var c = 3; c <= 15; c++)
                Assert.False(ws.Cell(row, c).IsEmpty(), $"{branch}: column {c} must be counted");
            Assert.Equal(1, ws.Cell(row, Col("İşçi sayı")).GetValue<int>());
            Assert.Equal(0, ws.Cell(row, Col("Qayıb")).GetValue<int>());
        }
    }

    [Fact]
    public void Every_bucket_lands_in_its_own_column_and_the_headcount_is_everybody_on_the_line()
    {
        var rows = new List<DayBoardSheet.Row>();
        rows.AddRange(People("Qala Anbar", "incomplete", 3));
        rows.AddRange(People("Qala Anbar", "rest", 1));
        rows.AddRange(People("Qala Anbar", "dayOff", 2));
        rows.AddRange(People("Qala Anbar", "onLeave", 4));
        rows.AddRange(People("Qala Anbar", "absent", 5));
        rows.AddRange(People("Qala Anbar", "permission", 6));
        rows.AddRange(People("Qala Anbar", "sick", 7));
        rows.AddRange(People("Qala Anbar", "trip", 8));
        rows.AddRange(People("Qala Anbar", "unpaid", 9));
        rows.AddRange(People("Qala Anbar", "pending", 10));
        rows.AddRange(People("Qala Anbar", "present", 11));
        rows.AddRange(People("Qala Anbar", "onboarding", 12));
        // A row from an older client with no bucket: in the headcount, in no column.
        rows.Add(new DayBoardSheet.Row("Köhnə", "", "Qala Anbar", "?", "", "", "", null));

        using var wb = Build(rows, Form);
        var ws = wb.Worksheet("Xülasə");
        var row = LineRow("Qala Anbar");
        int At(string heading) => ws.Cell(row, Col(heading)).GetValue<int>();

        Assert.Equal(3, At("İşdə"));
        Assert.Equal(1, At("İstirahət (təyin edilmiş)"));
        Assert.Equal(2, At("Həftəlik istirahət"));
        Assert.Equal(4, At("Məzuniyyət"));
        Assert.Equal(5, At("Qayıb"));
        Assert.Equal(6, At("İcazə"));
        Assert.Equal(7, At("Xəstəlik"));
        Assert.Equal(8, At("Ezamiyyət"));
        Assert.Equal(9, At("Ödənişsiz"));
        Assert.Equal(10, At("Növbəsi başlamayıb"));
        Assert.Equal(11, At("Tamamlayıb"));
        Assert.Equal(12, At("Aktivləşdirməyib"));
        Assert.Equal(79, At("İşçi sayı"));
    }

    [Fact]
    public void CEMI_adds_up_the_lines_that_have_people_and_the_blank_lines_add_nothing()
    {
        var rows = new List<DayBoardSheet.Row>();
        rows.AddRange(People("Heydər Əliyev Mərkəzi", "incomplete", 62));
        rows.AddRange(People("Heydər Əliyev Mərkəzi", "absent", 2));
        rows.AddRange(People("Fəvvarələr Meydanı", "incomplete", 39));
        rows.AddRange(People("Fəvvarələr Meydanı", "pending", 13));
        rows.AddRange(People("Axundov parkı", "present", 1));

        using var wb = Build(rows, Form);
        var ws = wb.Worksheet("Xülasə");
        int Total(string heading) => ws.Cell(30, Col(heading)).GetValue<int>();

        Assert.Equal(101, Total("İşdə"));
        Assert.Equal(2, Total("Qayıb"));
        Assert.Equal(13, Total("Növbəsi başlamayıb"));
        Assert.Equal(1, Total("Tamamlayıb"));
        Assert.Equal(0, Total("Məzuniyyət"));
        Assert.Equal(rows.Count, Total("İşçi sayı"));
        // The total is the sum of the lines above it, column by column.
        for (var c = 3; c <= 15; c++)
        {
            var sum = Enumerable.Range(3, 27).Sum(r => ws.Cell(r, c).IsEmpty() ? 0 : ws.Cell(r, c).GetValue<int>());
            Assert.Equal(sum, ws.Cell(30, c).GetValue<int>());
        }
    }

    [Fact]
    public void A_branch_not_in_this_export_is_a_blank_line_nothing_is_invented_or_borrowed()
    {
        // Two of the 24 chosen: the other branch lines have nobody in this file, so they say nothing.
        var rows = People("Qala Anbar", "present", 2).Concat(People("İDEA", "absent", 1)).ToList();

        using var wb = Build(rows, Form);
        var ws = wb.Worksheet("Xülasə");

        foreach (var line in Lines.Except(["Qala Anbar", "İDEA"]))
            Assert.True(ws.Range(LineRow(line), 3, LineRow(line), 15).IsEmpty(), $"{line} must be blank");
        Assert.Equal(3, ws.Cell(30, Col("İşçi sayı")).GetValue<int>());
    }

    [Fact]
    public void An_area_the_list_has_no_line_for_is_refused_never_dropped()
    {
        var rows = OnePerBranch();
        rows.Add(Person("Aeroport yolu", "present"));

        Assert.Equal(new[] { "Aeroport yolu" }, Form.Unlisted(rows.Select(r => r.Location)));
        Assert.Throws<ArgumentException>(() => DayBoardSheet.Build("t", rows, null, BoardWords, Form));
    }

    [Fact]
    public void A_branch_is_found_by_its_name_whatever_the_spacing_or_case()
    {
        using var wb = Build([Person("  qala anbar ", "absent"), Person("İDEA", "absent")], Form);
        var ws = wb.Worksheet("Xülasə");

        Assert.Empty(Form.Unlisted(["  qala anbar ", "İDEA"]));
        Assert.Equal(1, ws.Cell(LineRow("Qala Anbar"), Col("Qayıb")).GetValue<int>());
        // The line keeps the list's spelling.
        Assert.Equal("Qala Anbar", ws.Cell(LineRow("Qala Anbar"), 2).GetString());
    }

    [Fact]
    public void The_form_is_laid_out_like_the_list()
    {
        using var wb = Build(OnePerBranch(), Form);
        var ws = wb.Worksheet("Xülasə");

        var heading = ws.Cell(2, 13).Style;
        Assert.Equal("Times New Roman", heading.Font.FontName);
        Assert.Equal(12, heading.Font.FontSize);
        Assert.True(heading.Font.Bold);
        Assert.Equal(System.Drawing.Color.White.ToArgb(), heading.Font.FontColor.Color.ToArgb());
        Assert.Equal(XLColor.FromHtml("#1E70C8").Color.ToArgb(), heading.Fill.BackgroundColor.Color.ToArgb());
        Assert.True(heading.Alignment.WrapText);
        Assert.Equal(47.25, ws.Row(2).Height);

        var blank = ws.Cell(LineRow("Zoopark"), 7).Style;
        Assert.Equal("Times New Roman", blank.Font.FontName);
        Assert.Equal(XLBorderStyleValues.Thin, blank.Border.LeftBorder);
        Assert.Equal(XLBorderStyleValues.Thin, blank.Border.BottomBorder);
        Assert.Equal(XLAlignmentHorizontalValues.Center, ws.Cell(LineRow("Qala Anbar"), 2).Style.Alignment.Horizontal);

        var total = ws.Cell(30, 13).Style;
        Assert.True(total.Font.Bold);
        Assert.Equal(XLColor.FromHtml("#FFF4CE").Color.ToArgb(), total.Fill.BackgroundColor.Color.ToArgb());
        Assert.Equal(0, ws.SheetView.SplitRow);
    }

    [Fact]
    public void The_detail_sheet_does_not_change_with_the_form()
    {
        var rows = OnePerBranch();
        using var standard = Build(rows, null);
        using var form = Build(rows, Form);

        var a = standard.Worksheet("Davamiyyət");
        var b = form.Worksheet("Davamiyyət");
        Assert.Equal(a.LastRowUsed()!.RowNumber(), b.LastRowUsed()!.RowNumber());
        for (var r = 1; r <= a.LastRowUsed()!.RowNumber(); r++)
            for (var c = 1; c <= 8; c++)
                Assert.Equal(a.Cell(r, c).GetString(), b.Cell(r, c).GetString());
    }

    [Fact]
    public void Without_the_form_the_summary_is_the_standard_one()
    {
        using var wb = Build(OnePerBranch(), null);
        var ws = wb.Worksheet("Xülasə");

        Assert.Equal("Ərazi", ws.Cell(4, 1).GetString());
        Assert.Equal("İşçi sayı", ws.Cell(4, 2).GetString());
        Assert.Equal("Çıxış yoxdur", ws.Cell(4, 4).GetString());
        Assert.Equal(24 + 4 + 1, ws.LastRowUsed()!.RowNumber());
    }

    [Fact]
    public void The_list_is_Baki_Abadliqs_alone_and_is_the_owners_list_line_for_line()
    {
        Assert.Equal(Lines, DaySummaryTemplates.For(BakiAbadliq).Single().Areas);
        Assert.Equal(DaySummaryTemplates.DavamiyyetSiyahisi, DaySummaryTemplates.For(BakiAbadliq).Single().Key);
        Assert.Empty(DaySummaryTemplates.For(Guid.NewGuid()));
        Assert.Null(DaySummaryTemplates.Find(Guid.NewGuid(), DaySummaryTemplates.DavamiyyetSiyahisi));
        Assert.Null(DaySummaryTemplates.Find(BakiAbadliq, "another-form"));
        Assert.Equal(24, Branches.Length);
    }

    // ─── the endpoint: who may have the form, and what it refuses ───────────────────────────────────

    private static ReportsController Controller(Guid tenant)
    {
        var context = new TenantContext();
        context.Resolve(tenant);
        // ExportDay and DayTemplates read nothing but the tenant; the other services are never reached.
        return new ReportsController(null!, null!, null!, context);
    }

    private static ExportDayRequest Request(IEnumerable<string> areas, string? view, string? template)
        => new("Davamiyyət", "2026-10-21",
            areas.Select(a => new ExportDayRow("Ad", a, "İşdə", "07:30", "", "var", "incomplete")).ToList(),
            BucketLabels: BoardWords, View: view, Template: template);

    private static string? ErrorOf(IActionResult result)
        => (result as BadRequestObjectResult)?.Value?.GetType().GetProperty("error")?.GetValue(((BadRequestObjectResult)result).Value) as string;

    [Fact]
    public void The_form_is_printed_for_Baki_Abadliq_when_chosen()
    {
        var result = Controller(BakiAbadliq).ExportDay(Request(Branches, "actual", DaySummaryTemplates.DavamiyyetSiyahisi));

        var file = Assert.IsType<FileContentResult>(result);
        using var wb = new XLWorkbook(new MemoryStream(file.FileContents));
        Assert.Equal("№", wb.Worksheet("Xülasə").Cell(2, 1).GetString());
    }

    [Fact]
    public void Another_company_cannot_have_the_form_by_naming_it()
        => Assert.Equal("UnknownTemplate",
            ErrorOf(Controller(Guid.NewGuid()).ExportDay(Request(Branches, "actual", DaySummaryTemplates.DavamiyyetSiyahisi))));

    [Fact]
    public void The_form_is_only_for_the_branch_view()
        => Assert.Equal("TemplateNeedsActualView",
            ErrorOf(Controller(BakiAbadliq).ExportDay(Request(Branches, "paper", DaySummaryTemplates.DavamiyyetSiyahisi))));

    [Fact]
    public void An_export_with_an_area_outside_the_form_is_refused_with_the_area_named()
    {
        var result = Controller(BakiAbadliq).ExportDay(
            Request(Branches.Append("Ramana Yaşıllıq"), "actual", DaySummaryTemplates.DavamiyyetSiyahisi));

        Assert.Equal("AreaNotInTemplate", ErrorOf(result));
        var areas = ((BadRequestObjectResult)result).Value!.GetType().GetProperty("areas")!.GetValue(((BadRequestObjectResult)result).Value);
        Assert.Equal(new[] { "Ramana Yaşıllıq" }, Assert.IsAssignableFrom<IEnumerable<string>>(areas));
    }

    [Fact]
    public void Without_a_choice_the_export_is_the_standard_one_for_everybody()
    {
        var result = Controller(BakiAbadliq).ExportDay(Request(Branches.Append("Ramana Yaşıllıq"), "actual", null));

        var file = Assert.IsType<FileContentResult>(result);
        using var wb = new XLWorkbook(new MemoryStream(file.FileContents));
        Assert.Equal("Ərazi", wb.Worksheet("Xülasə").Cell(4, 1).GetString());
    }

    [Fact]
    public void Only_Baki_Abadliq_is_offered_the_form()
    {
        var mine = Assert.IsType<OkObjectResult>(Controller(BakiAbadliq).DayTemplates());
        var other = Assert.IsType<OkObjectResult>(Controller(Guid.NewGuid()).DayTemplates());

        Assert.Single(Assert.IsAssignableFrom<IEnumerable<object>>(mine.Value));
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<object>>(other.Value));
    }
}
