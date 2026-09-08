using AttendanceQR.Application.Reporting;
using ClosedXML.Excel;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The report export's first sheet: one line per site for the whole period.
///
/// The file used to be a single flat list of every employee in the company, and the person it is
/// sent to rebuilt it into this table by hand before reading it. These tests hold the shape of the
/// sheet they asked for — and, more importantly, that its CƏMİ line agrees with the lines above it.
/// A total that can disagree with its own table is worse than no total: the reader cannot tell which
/// of the two is lying, so they stop trusting both.
/// </summary>
public class RangeReportSummaryTests
{
    private static EmployeeReportRow Row(
        string name, string site, int workDays = 0, int absent = 0, double hours = 0,
        double overtime = 0, int vacation = 0, int sick = 0, int unpaid = 0, int rest = 0,
        int trip = 0, int permission = 0, string? paperEmployer = null, string? paperSite = null)
        => new(
            Guid.NewGuid(), name, site, workDays, LateCount: 0, absent, IncompleteDays: 0,
            hours, overtime, LeaveDays: vacation + sick + unpaid, trip, permission,
            EarlyLeaveHours: 0, EarlyArriveHours: 0,
            VacationDays: vacation, SickDays: sick, UnpaidDays: unpaid, RestDays: rest,
            PaperEmployer: paperEmployer, PaperSite: paperSite);

    // Xülasə sheet columns, so a shifted column fails by name rather than by a bare number.
    private const int ColHeadcount = 2, ColPaper = 3, ColWorkDays = 4, ColAbsent = 5, ColHours = 6;
    private const int ColVacation = 8, ColSick = 9, ColLast = 13;

    private static XLWorkbook Export(params EmployeeReportRow[] rows)
    {
        var totals = new ReportTotals(
            rows.Sum(r => r.WorkDays), 0, rows.Sum(r => r.AbsentDays), 0,
            rows.Sum(r => r.TotalWorkedHours), rows.Sum(r => r.OvertimeHours),
            rows.Sum(r => r.LeaveDays), rows.Sum(r => r.TripDays), rows.Sum(r => r.PermissionDays));

        var report = new AttendanceReport(
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 8), "Bütün filiallar", rows, totals);

        var bytes = new ExcelReportExporter().Build(report);
        return new XLWorkbook(new MemoryStream(bytes));
    }

    [Fact]
    public void Summary_is_the_first_sheet_the_file_opens_on()
    {
        // It is what the file is opened for. Behind it, the same people, one row each.
        using var wb = Export(Row("Əliyev A", "Novxanı"));

        Assert.Equal("Xülasə", wb.Worksheet(1).Name);
        Assert.Equal("Davamiyyət", wb.Worksheet(2).Name);
    }

    [Fact]
    public void One_line_per_site_carrying_that_sites_own_figures()
    {
        using var wb = Export(
            Row("Bir", "Novxanı", workDays: 6, absent: 1, hours: 48, vacation: 2),
            Row("İki", "Novxanı", workDays: 5, hours: 40, sick: 1),
            Row("Üç", "Bayıl yolu", workDays: 7, hours: 56, rest: 1));

        var ws = wb.Worksheet("Xülasə");
        var novxani = FindRow(ws, "Novxanı");

        Assert.Equal(2, ws.Cell(novxani, ColHeadcount).GetValue<int>());
        Assert.Equal(11, ws.Cell(novxani, ColWorkDays).GetValue<int>());
        Assert.Equal(1, ws.Cell(novxani, ColAbsent).GetValue<int>());
        Assert.Equal(88, ws.Cell(novxani, ColHours).GetValue<double>());
        Assert.Equal(2, ws.Cell(novxani, ColVacation).GetValue<int>());
        Assert.Equal(1, ws.Cell(novxani, ColSick).GetValue<int>());
    }

    [Fact]
    public void The_total_line_adds_up_to_the_lines_above_it()
    {
        // The one that matters. Every column of CƏMİ must be the sum of the site lines printed over
        // it — not a figure fetched from somewhere else that happens to be near it.
        using var wb = Export(
            Row("Bir", "Novxanı", workDays: 6, absent: 1, hours: 48, vacation: 2, trip: 1),
            Row("İki", "Bayıl yolu", workDays: 5, absent: 2, hours: 40, sick: 1, permission: 3),
            Row("Üç", "Qala Anbar", workDays: 7, hours: 56, rest: 4, unpaid: 1));

        var ws = wb.Worksheet("Xülasə");
        var cemi = FindRow(ws, "CƏMİ");
        var sites = Enumerable.Range(5, cemi - 5).ToList();   // header is row 4; sites start at 5

        Assert.Equal(3, sites.Count);
        for (var col = ColHeadcount; col <= ColLast; col++)
            Assert.Equal(
                sites.Sum(r => ws.Cell(r, col).GetValue<double>()),
                ws.Cell(cemi, col).GetValue<double>());
    }

    [Fact]
    public void Sites_are_ordered_the_Azerbaijani_way()
    {
        // «Ə» sits after E in the alphabet and after Z in a naive sort, which would push a large
        // share of these sites to the bottom of every summary.
        using var wb = Export(Row("a", "Zığ"), Row("b", "Əmircan"), Row("c", "Bayıl yolu"));

        var ws = wb.Worksheet("Xülasə");
        Assert.Equal("Bayıl yolu", ws.Cell(5, 1).GetString());
        Assert.Equal("Əmircan", ws.Cell(6, 1).GetString());
        Assert.Equal("Zığ", ws.Cell(7, 1).GetString());
    }

    [Fact]
    public void A_row_with_no_site_still_lands_on_a_line()
    {
        // It is somebody's month. Dropping it would make the summary disagree with the sheet behind
        // it, and silently — the reader would never know a person was missing.
        using var wb = Export(Row("Kimsə", "", workDays: 4));

        var ws = wb.Worksheet("Xülasə");
        Assert.Equal("—", ws.Cell(5, 1).GetString());
        Assert.Equal(4, ws.Cell(5, ColWorkDays).GetValue<int>());
    }

    [Theory]
    [InlineData(2026, 9, 1, 2026, 9, 8, "1–8 sentyabr 2026")]
    [InlineData(2026, 9, 8, 2026, 9, 8, "8 sentyabr 2026")]
    [InlineData(2026, 8, 28, 2026, 9, 8, "28 avqust – 8 sentyabr 2026")]
    [InlineData(2025, 12, 28, 2026, 1, 8, "28 dekabr 2025 – 8 yanvar 2026")]
    public void The_period_is_written_the_way_somebody_says_it(
        int y1, int m1, int d1, int y2, int m2, int d2, string expected)
    {
        var report = new AttendanceReport(
            new DateOnly(y1, m1, d1), new DateOnly(y2, m2, d2), "Bütün filiallar",
            [Row("Bir", "Novxanı")],
            new ReportTotals(0, 0, 0, 0, 0, 0, 0, 0, 0));

        using var wb = new XLWorkbook(new MemoryStream(new ExcelReportExporter().Build(report)));

        Assert.Equal($"Davamiyyət hesabatı — {expected}", wb.Worksheet("Xülasə").Cell(1, 1).GetString());
    }

    [Fact]
    public void The_summary_counts_how_many_of_a_site_are_on_another_companys_books()
    {
        // The owner's question in one cell: of the people working here, how many belong to somebody
        // else on paper. Çingiz Hümbətov works at Green Garden and is on Bakı Abadlıq's payroll.
        using var wb = Export(
            Row("Çingiz Hümbətov", "Green Garden", workDays: 6,
                paperEmployer: "Bakı Abadlıq Xidməti", paperSite: "Nərimanov Ofis"),
            Row("Öz adamı", "Green Garden", workDays: 6),
            Row("O biri", "Bayıl yolu", workDays: 6));

        var ws = wb.Worksheet("Xülasə");
        Assert.Equal(1, ws.Cell(FindRow(ws, "Green Garden"), ColPaper).GetValue<int>());
        Assert.Equal(0, ws.Cell(FindRow(ws, "Bayıl yolu"), ColPaper).GetValue<int>());
        Assert.Equal(1, ws.Cell(FindRow(ws, "CƏMİ"), ColPaper).GetValue<int>());
    }

    [Fact]
    public void The_detail_sheet_names_the_employer_and_the_site()
    {
        using var wb = Export(
            Row("Çingiz Hümbətov", "Green Garden",
                paperEmployer: "Bakı Abadlıq Xidməti", paperSite: "Nərimanov Ofis"),
            Row("Yalnız şirkət", "Green Garden", paperEmployer: "CleanFix"),
            Row("Öz adamı", "Green Garden"));

        var ws = wb.Worksheet("Davamiyyət");
        var cell = (string name) =>
        {
            for (var r = 6; r <= 40; r++)
                if (ws.Cell(r, 1).GetString() == name) return ws.Cell(r, 3).GetString();
            throw new Xunit.Sdk.XunitException($"«{name}» tapılmadı.");
        };

        Assert.Equal("Bakı Abadlıq Xidməti / Nərimanov Ofis", cell("Çingiz Hümbətov"));
        Assert.Equal("CleanFix", cell("Yalnız şirkət"));
        // Empty, not a dash: the column has to stay quiet on the many so the eye lands on the few.
        Assert.Equal(string.Empty, cell("Öz adamı"));
    }

    private static int FindRow(IXLWorksheet ws, string label)
    {
        for (var r = 5; r <= 60; r++)
            if (ws.Cell(r, 1).GetString() == label) return r;
        throw new Xunit.Sdk.XunitException($"«{label}» sətri tapılmadı.");
    }
}
