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
    private static readonly string[] Headers =
        {
            "Employee", "Location", "Work Days", "Absent Days", "Total Hours", "Overtime Hours",
            "Leave Days", "Permission Days"
        };

    public byte[] Build(AttendanceReport report)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Attendance");

        // Title block.
        var title = ws.Range(1, 1, 1, Headers.Length).Merge();
        title.Value = "Attendance Report";
        title.Style.Font.Bold = true;
        title.Style.Font.FontSize = 14;

        ws.Cell(2, 1).Value = $"Scope: {report.ScopeLabel}";
        ws.Cell(3, 1).Value = $"Period: {report.From:yyyy-MM-dd} — {report.To:yyyy-MM-dd}";

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
            ws.Cell(r, 7).Value = row.LeaveDays;
            ws.Cell(r, 8).Value = row.PermissionDays;
            for (var c = 1; c <= Headers.Length; c++)
                ws.Cell(r, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            r++;
        }

        // Totals row.
        ws.Cell(r, 1).Value = "TOTAL";
        ws.Cell(r, 3).Value = report.Totals.WorkDays;
        ws.Cell(r, 4).Value = report.Totals.AbsentDays;
        ws.Cell(r, 5).Value = report.Totals.TotalWorkedHours;
        ws.Cell(r, 6).Value = report.Totals.OvertimeHours;
        ws.Cell(r, 7).Value = report.Totals.LeaveDays;
        ws.Cell(r, 8).Value = report.Totals.PermissionDays;
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
    private static readonly string[] PayrollHeaders =
        {
            "İşçi", "Filial", "Aylıq maaş", "İş günü", "Gəlib", "Qayıb", "Məzuniyyət/İcazə",
            "Əlavə saat", "Günlük", "Çıxılan", "Ödəniləcək"
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
                ws.Cell(r, 9).Value = row.PerDay;
                ws.Cell(r, 10).Value = row.Deduction;
                ws.Cell(r, 11).Value = row.Payable;
            }
            ws.Cell(r, 4).Value = row.ScheduledDays;
            ws.Cell(r, 5).Value = row.WorkDays;
            ws.Cell(r, 6).Value = row.AbsentDays;
            ws.Cell(r, 7).Value = row.LeaveDays + row.PermissionDays;
            ws.Cell(r, 8).Value = row.OvertimeHours;
            foreach (var col in new[] { 3, 9, 10, 11 })
                ws.Cell(r, col).Style.NumberFormat.Format = Money;
            for (var c = 1; c <= PayrollHeaders.Length; c++)
                ws.Cell(r, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            r++;
        }

        ws.Cell(r, 1).Value = "CƏMİ";
        ws.Cell(r, 3).Value = report.TotalMonthlySalary;
        ws.Cell(r, 10).Value = report.TotalDeduction;
        ws.Cell(r, 11).Value = report.TotalPayable;
        foreach (var col in new[] { 3, 10, 11 })
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