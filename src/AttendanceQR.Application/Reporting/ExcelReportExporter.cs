using System.Globalization;
using ClosedXML.Excel;

namespace AttendanceQR.Application.Reporting;

public interface IExcelReportExporter
{
    byte[] Build(AttendanceReport report);

    /// <summary>Renders the payroll (Maaş) report to a formatted .xlsx — AZN money columns, the
    /// accountant's hand-off.</summary>
    byte[] BuildPayroll(PayrollReport report);

    /// <summary>Renders the monthly timesheet (Tabel) — the days-across-employees grid the accountant
    /// reconciles, with the code legend on the sheet so it stands on its own once printed.</summary>
    byte[] BuildTabel(TabelReport report);
}

/// <summary>Renders an <see cref="AttendanceReport"/> to a formatted .xlsx via ClosedXML (MIT).</summary>
public sealed class ExcelReportExporter : IExcelReportExporter
{
    // No "Late Count": every employee keeps their own hours, so a location-wide shift cannot say who
    // was late. AttendanceReport still carries the figure — only the sheet omits it.
    // Azerbaijani, like every other thing a person in this product reads. This sheet was the last
    // English surface left, and it is a file that gets printed and handed to somebody.
    //
    // Leave is FIVE columns, not one. It was «Leave Days» — a single number in which «Məzuniyyət»,
    // «Xəstəlik», «Ödənişsiz» and «İstirahət» were indistinguishable, and the reader could only read
    // it as annual leave. On production that one column was hiding 495 days of annual leave, 415 of
    // rest, 31 of sick and 23 unpaid. They are four different entitlements — sick needs a medical
    // certificate and comes from a different budget; unpaid is not paid at all — and an accountant
    // cannot recover one from the other after the fact.
    private static readonly string[] Headers =
        {
            "İşçi", "Filial", "İş günü", "Qayıb", "İşlənmiş saat", "Əlavə saat",
            "Məzuniyyət", "Xəstəlik", "Ödənişsiz", "İstirahət", "Ezamiyyət", "İcazə",
            "Tez çıxma (saat)", "Tez gəlmə (saat)"
        };

    /// <summary>
    /// The site-by-site line the summary sheet is made of. Same shape as the detail sheet's columns,
    /// minus the two early-hours diagnostics — a summary that repeats every column of the table under
    /// it is not a summary, and nobody decides anything from "tez gəlmə" totalled across a park.
    /// </summary>
    private static readonly string[] SummaryHeaders =
        {
            "Ərazi", "İşçi sayı", "İş günü", "Qayıb", "İşlənmiş saat", "Əlavə saat",
            "Məzuniyyət", "Xəstəlik", "Ödənişsiz", "İstirahət", "Ezamiyyət", "İcazə"
        };

    // The daily board's workbook (DayBoardSheet) already prints a summary in these colours, and this
    // report is read by the same people on the same morning. Two summary sheets of the same company
    // that look like they came from different products is how a reader starts checking one against
    // the other instead of reading either.
    private static readonly XLColor HeaderBlue = XLColor.FromHtml("#1E70C8");
    private static readonly XLColor TotalBand = XLColor.FromHtml("#FFF4CE");

    /// <summary>Azerbaijani collation, so «Ə» and «İ» sort where a reader expects rather than after Z.</summary>
    private static readonly StringComparer Az = BuildAz();

    private static StringComparer BuildAz()
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

    public byte[] Build(AttendanceReport report)
    {
        using var workbook = new XLWorkbook();

        // The summary goes FIRST, because it is what the file is opened for. Until now this export
        // was one flat list of every employee in the company, and the reader's first act on receiving
        // it was to rebuild it into exactly this table by hand — the same complaint that produced the
        // daily board's two-sheet workbook. The period figures are per SITE here: "how did Fəvvarələr
        // do this month" is answerable on one screen instead of by filtering four hundred rows.
        BuildRangeSummary(workbook, report);

        var ws = workbook.Worksheets.Add("Davamiyyət");

        // Title block.
        var title = ws.Range(1, 1, 1, Headers.Length).Merge();
        title.Value = $"Davamiyyət hesabatı — {PeriodLabel(report.From, report.To)}";
        title.Style.Font.Bold = true;
        title.Style.Font.FontSize = 14;

        ws.Cell(2, 1).Value = $"Əhatə: {report.ScopeLabel}";
        ws.Cell(3, 1).Value = $"Dövr: {report.From:dd.MM.yyyy} — {report.To:dd.MM.yyyy}";

        // Header row.
        const int headerRow = 5;
        for (var c = 0; c < Headers.Length; c++)
        {
            var cell = ws.Cell(headerRow, c + 1);
            cell.Value = Headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        // Data rows.
        var r = headerRow + 1;
        foreach (var row in report.Rows)
        {
            ws.Cell(r, 1).Value = row.EmployeeName;
            ws.Cell(r, 2).Value = row.LocationName;
            ws.Cell(r, 3).Value = row.WorkDays;
            ws.Cell(r, 4).Value = row.AbsentDays;
            ws.Cell(r, 5).Value = row.TotalWorkedHours;
            ws.Cell(r, 6).Value = row.OvertimeHours;
            ws.Cell(r, 7).Value = row.VacationDays;
            ws.Cell(r, 8).Value = row.SickDays;
            ws.Cell(r, 9).Value = row.UnpaidDays;
            ws.Cell(r, 10).Value = row.RestDays;
            ws.Cell(r, 11).Value = row.TripDays;
            ws.Cell(r, 12).Value = row.PermissionDays;
            ws.Cell(r, 13).Value = row.EarlyLeaveHours;
            ws.Cell(r, 14).Value = row.EarlyArriveHours;
            for (var c = 1; c <= Headers.Length; c++)
                ws.Cell(r, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            r++;
        }

        // Totals row.
        ws.Cell(r, 1).Value = "CƏMİ";
        ws.Cell(r, 3).Value = report.Totals.WorkDays;
        ws.Cell(r, 4).Value = report.Totals.AbsentDays;
        ws.Cell(r, 5).Value = report.Totals.TotalWorkedHours;
        ws.Cell(r, 6).Value = report.Totals.OvertimeHours;
        ws.Cell(r, 7).Value = report.Totals.VacationDays;
        ws.Cell(r, 8).Value = report.Totals.SickDays;
        ws.Cell(r, 9).Value = report.Totals.UnpaidDays;
        ws.Cell(r, 10).Value = report.Totals.RestDays;
        ws.Cell(r, 11).Value = report.Totals.TripDays;
        ws.Cell(r, 12).Value = report.Totals.PermissionDays;
        ws.Cell(r, 13).Value = report.Totals.EarlyLeaveHours;
        ws.Cell(r, 14).Value = report.Totals.EarlyArriveHours;
        var totalRange = ws.Range(r, 1, r, Headers.Length);
        totalRange.Style.Font.Bold = true;
        totalRange.Style.Fill.BackgroundColor = XLColor.LightYellow;
        totalRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    // Azerbaijani + AZN — this sheet goes straight to the accountant, so it speaks their language.
    /// <summary>
    /// One line per site for the whole period, and a CƏMİ line under them.
    ///
    /// Every figure is summed from the SAME rows the detail sheet prints — never from a second query.
    /// A summary that can disagree with the table behind it is worse than no summary, because the
    /// reader has no way to tell which of the two is lying.
    /// </summary>
    private static void BuildRangeSummary(XLWorkbook wb, AttendanceReport report)
    {
        var ws = wb.Worksheets.Add("Xülasə");
        var cols = SummaryHeaders.Length;

        ws.Cell(1, 1).Value = $"Davamiyyət hesabatı — {PeriodLabel(report.From, report.To)}";
        ws.Range(1, 1, 1, cols).Merge();
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        ws.Cell(2, 1).Value = $"Əhatə: {report.ScopeLabel}";
        ws.Range(2, 1, 2, cols).Merge();
        ws.Cell(2, 1).Style.Font.Italic = true;
        ws.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#555555");
        ws.Cell(2, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        const int headerRow = 4;
        for (var i = 0; i < cols; i++)
            ws.Cell(headerRow, i + 1).Value = SummaryHeaders[i];

        var head = ws.Range(headerRow, 1, headerRow, cols);
        head.Style.Font.Bold = true;
        head.Style.Fill.BackgroundColor = HeaderBlue;
        head.Style.Font.FontColor = XLColor.White;
        head.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        head.Style.Alignment.WrapText = true;

        var groups = report.Rows
            .GroupBy(x => string.IsNullOrWhiteSpace(x.LocationName) ? "—" : x.LocationName)
            .OrderBy(g => g.Key, Az)
            .ToList();

        var r = headerRow + 1;
        foreach (var g in groups)
        {
            Write(r, g.Key, g.ToList());
            r++;
        }

        // Summed from the lines above, not from report.Totals: this sheet's own table has to add up.
        ws.Cell(r, 1).Value = "CƏMİ";
        WriteFigures(r, report.Rows);
        var total = ws.Range(r, 1, r, cols);
        total.Style.Font.Bold = true;
        total.Style.Fill.BackgroundColor = TotalBand;

        var table = ws.Range(headerRow, 1, r, cols);
        table.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        table.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        ws.Range(headerRow + 1, 2, r, cols).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        ws.Column(1).Width = 30;
        for (var c = 2; c <= cols; c++)
            ws.Column(c).Width = 12;
        ws.SheetView.FreezeRows(headerRow);

        void Write(int row, string site, IReadOnlyCollection<EmployeeReportRow> people)
        {
            ws.Cell(row, 1).Value = site;
            WriteFigures(row, people);
        }

        void WriteFigures(int row, IReadOnlyCollection<EmployeeReportRow> people)
        {
            // Headcount counts PEOPLE, not rows — the report is one row per employee today, and a
            // distinct count keeps that true if it ever stops being.
            ws.Cell(row, 2).Value = people.Select(x => x.EmployeeId).Distinct().Count();
            ws.Cell(row, 3).Value = people.Sum(x => x.WorkDays);
            ws.Cell(row, 4).Value = people.Sum(x => x.AbsentDays);
            ws.Cell(row, 5).Value = Math.Round(people.Sum(x => x.TotalWorkedHours), 1);
            ws.Cell(row, 6).Value = Math.Round(people.Sum(x => x.OvertimeHours), 1);
            ws.Cell(row, 7).Value = people.Sum(x => x.VacationDays);
            ws.Cell(row, 8).Value = people.Sum(x => x.SickDays);
            ws.Cell(row, 9).Value = people.Sum(x => x.UnpaidDays);
            ws.Cell(row, 10).Value = people.Sum(x => x.RestDays);
            ws.Cell(row, 11).Value = people.Sum(x => x.TripDays);
            ws.Cell(row, 12).Value = people.Sum(x => x.PermissionDays);
        }
    }

    /// <summary>
    /// «1–8 sentyabr 2026» — the period as somebody says it out loud, with the month and year written
    /// once when they are shared. «2026-09-01 — 2026-09-08» is still on the sheet below for anyone
    /// reconciling; this is the line at the top of the page.
    /// </summary>
    private static string PeriodLabel(DateOnly from, DateOnly to)
    {
        var month = AzMonths.Select(m => m.ToLowerInvariant()).ToArray();

        if (from == to) return $"{from.Day} {month[from.Month - 1]} {from.Year}";
        if (from.Year != to.Year)
            return $"{from.Day} {month[from.Month - 1]} {from.Year} – {to.Day} {month[to.Month - 1]} {to.Year}";
        if (from.Month != to.Month)
            return $"{from.Day} {month[from.Month - 1]} – {to.Day} {month[to.Month - 1]} {to.Year}";
        return $"{from.Day}–{to.Day} {month[from.Month - 1]} {from.Year}";
    }

    private static readonly string[] PayrollHeaders =
        {
            "İşçi", "Filial", "Aylıq maaş", "İş günü", "Gəlib", "Qayıb",
            // «Məzuniyyət/İcazə» was one column holding four entitlements plus permission hours.
            // This sheet goes to an accountant, who has to treat sick and unpaid differently from
            // annual leave and cannot un-add them once they are summed.
            "Məzuniyyət", "Xəstəlik", "Ödənişsiz", "İstirahət", "Ezamiyyət", "İcazə",
            "Əlavə saat", "Günlük", "Çıxılan", "Ödəniləcək", "Tez çıxma (saat)", "Tez gəlmə (saat)"
        };
    private const string Money = "#,##0.00";

    public byte[] BuildPayroll(PayrollReport report)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Maaş");

        var title = ws.Range(1, 1, 1, PayrollHeaders.Length).Merge();
        title.Value = "Maaş hesabatı";
        title.Style.Font.Bold = true;
        title.Style.Font.FontSize = 14;

        ws.Cell(2, 1).Value = $"Əhatə: {report.ScopeLabel}";
        ws.Cell(3, 1).Value = $"Dövr: {report.From:yyyy-MM-dd} — {report.To:yyyy-MM-dd}";

        const int headerRow = 5;
        for (var c = 0; c < PayrollHeaders.Length; c++)
        {
            var cell = ws.Cell(headerRow, c + 1);
            cell.Value = PayrollHeaders[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        var r = headerRow + 1;
        foreach (var row in report.Rows)
        {
            ws.Cell(r, 1).Value = row.EmployeeName;
            ws.Cell(r, 2).Value = row.LocationName;
            // Salary not set → leave the money cells blank rather than print a misleading 0.
            if (row.MonthlySalary is { } salary)
            {
                ws.Cell(r, 3).Value = salary;
                ws.Cell(r, 14).Value = row.PerDay;
                ws.Cell(r, 15).Value = row.Deduction;
                ws.Cell(r, 16).Value = row.Payable;
            }
            ws.Cell(r, 4).Value = row.ScheduledDays;
            ws.Cell(r, 5).Value = row.WorkDays;
            ws.Cell(r, 6).Value = row.AbsentDays;
            ws.Cell(r, 7).Value = row.VacationDays;
            ws.Cell(r, 8).Value = row.SickDays;
            ws.Cell(r, 9).Value = row.UnpaidDays;
            ws.Cell(r, 10).Value = row.RestDays;
            ws.Cell(r, 11).Value = row.TripDays;
            ws.Cell(r, 12).Value = row.PermissionDays;
            ws.Cell(r, 13).Value = row.OvertimeHours;
            ws.Cell(r, 17).Value = row.EarlyLeaveHours;
            ws.Cell(r, 18).Value = row.EarlyArriveHours;
            foreach (var col in new[] { 3, 14, 15, 16 })
                ws.Cell(r, col).Style.NumberFormat.Format = Money;
            for (var c = 1; c <= PayrollHeaders.Length; c++)
                ws.Cell(r, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            r++;
        }

        ws.Cell(r, 1).Value = "CƏMİ";
        ws.Cell(r, 3).Value = report.TotalMonthlySalary;
        ws.Cell(r, 15).Value = report.TotalDeduction;
        ws.Cell(r, 16).Value = report.TotalPayable;
        foreach (var col in new[] { 3, 15, 16 })
            ws.Cell(r, col).Style.NumberFormat.Format = Money;
        var totalRange = ws.Range(r, 1, r, PayrollHeaders.Length);
        totalRange.Style.Font.Bold = true;
        totalRange.Style.Fill.BackgroundColor = XLColor.LightYellow;
        totalRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static readonly string[] AzMonths =
    {
        "Yanvar", "Fevral", "Mart", "Aprel", "May", "İyun",
        "İyul", "Avqust", "Sentyabr", "Oktyabr", "Noyabr", "Dekabr"
    };

    public byte[] BuildTabel(TabelReport report)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Tabel");
        var days = report.DaysInMonth;
        // Columns: name, position, then one per day, then three totals.
        var totalCols = 2 + days + 3;

        // Title across the whole grid.
        var monthName = AzMonths[Math.Clamp(report.Month - 1, 0, 11)];
        ws.Cell(1, 1).Value = $"Tabel — {monthName} {report.Year} · {report.ScopeLabel}";
        ws.Range(1, 1, 1, totalCols).Merge();
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;

        // Header row: day numbers, then totals.
        var hr = 2;
        ws.Cell(hr, 1).Value = "İşçi";
        ws.Cell(hr, 2).Value = "Vəzifə";
        for (var d = 1; d <= days; d++)
            ws.Cell(hr, 2 + d).Value = d;
        ws.Cell(hr, 2 + days + 1).Value = "İş günü";
        ws.Cell(hr, 2 + days + 2).Value = "Qayıb";
        ws.Cell(hr, 2 + days + 3).Value = "Saat";

        var header = ws.Range(hr, 1, hr, totalCols);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#1E70C8");
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        var row = hr + 1;
        foreach (var r in report.Rows)
        {
            ws.Cell(row, 1).Value = r.EmployeeName;
            ws.Cell(row, 2).Value = r.Position ?? "";
            for (var d = 0; d < days; d++)
            {
                var cell = ws.Cell(row, 3 + d);
                cell.Value = r.Days[d];
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
            ws.Cell(row, 2 + days + 1).Value = r.WorkedDays;
            ws.Cell(row, 2 + days + 2).Value = r.AbsentDays;
            ws.Cell(row, 2 + days + 3).Value = r.WorkedHours;
            row++;
        }

        if (row > hr + 1)
        {
            var table = ws.Range(hr, 1, row - 1, totalCols);
            table.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            table.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        }

        // Legend below the grid, so a printed sheet explains its own codes.
        var lr = row + 1;
        ws.Cell(lr, 1).Value = "İşarələr:";
        ws.Cell(lr, 1).Style.Font.Bold = true;
        lr++;
        foreach (var item in report.Legend)
        {
            ws.Cell(lr, 1).Value = item.Code;
            ws.Cell(lr, 1).Style.Font.Bold = true;
            ws.Cell(lr, 2).Value = item.Label;
            lr++;
        }

        ws.Column(1).Width = 26;
        ws.Column(2).Width = 16;
        for (var d = 0; d < days; d++)
            ws.Column(3 + d).Width = 4;
        ws.SheetView.FreezeColumns(2);
        ws.SheetView.FreezeRows(2);

        using var s2 = new MemoryStream();
        workbook.SaveAs(s2);
        return s2.ToArray();
    }
}