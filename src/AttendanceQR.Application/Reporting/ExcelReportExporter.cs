using ClosedXML.Excel;

namespace AttendanceQR.Application.Reporting;

public interface IExcelReportExporter
{
    byte[] Build(AttendanceReport report);
}

/// <summary>Renders an <see cref="AttendanceReport"/> to a formatted .xlsx via ClosedXML (MIT).</summary>
public sealed class ExcelReportExporter : IExcelReportExporter
{
    private static readonly string[] Headers =
        {
            "Employee", "Location", "Work Days", "Late Count", "Absent Days", "Total Hours", "Overtime Hours",
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
            ws.Cell(r, 4).Value = row.LateCount;
            ws.Cell(r, 5).Value = row.AbsentDays;
            ws.Cell(r, 6).Value = row.TotalWorkedHours;
            ws.Cell(r, 7).Value = row.OvertimeHours;
            ws.Cell(r, 8).Value = row.LeaveDays;
            ws.Cell(r, 9).Value = row.PermissionDays;
            for (var c = 1; c <= Headers.Length; c++)
                ws.Cell(r, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            r++;
        }

        // Totals row.
        ws.Cell(r, 1).Value = "TOTAL";
        ws.Cell(r, 3).Value = report.Totals.WorkDays;
        ws.Cell(r, 4).Value = report.Totals.LateCount;
        ws.Cell(r, 5).Value = report.Totals.AbsentDays;
        ws.Cell(r, 6).Value = report.Totals.TotalWorkedHours;
        ws.Cell(r, 7).Value = report.Totals.OvertimeHours;
        ws.Cell(r, 8).Value = report.Totals.LeaveDays;
        ws.Cell(r, 9).Value = report.Totals.PermissionDays;
        var totalRange = ws.Range(r, 1, r, Headers.Length);
        totalRange.Style.Font.Bold = true;
        totalRange.Style.Fill.BackgroundColor = XLColor.LightYellow;
        totalRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
