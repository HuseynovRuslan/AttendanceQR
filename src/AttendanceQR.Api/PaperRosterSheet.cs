using ClosedXML.Excel;

namespace AttendanceQR.Api;

/// <summary>
/// The «sənəd üzrə» roster as a workbook.
///
/// Pure, and separate from the controller that fetches the rows, for the same reason
/// <see cref="DayBoardSheet"/> is: a file the accountant reconciles against a payroll has to be
/// testable without a database. The one judgement it makes is which rows to band — the ones whose
/// paper employer differs from the company actually running their account.
/// </summary>
public static class PaperRosterSheet
{
    /// <summary>One person as the paper sees them, beside where they actually stand.</summary>
    public sealed record Row(
        string FullName,
        string? Position,
        string PaperEmployer,
        string? PaperSite,
        string ActualCompany,
        string ActualSite,
        string? PhoneNumber,
        bool IsActive,
        bool Elsewhere);

    private static readonly string[] Headers =
    [
        "Ad Soyad", "Vəzifə", "Sənəd üzrə şirkət", "Sənəd üzrə ərazi",
        "Faktiki şirkət", "Faktiki ərazi", "Telefon", "Status", "Fərq",
    ];

    /// <summary>
    /// One sheet, grouped by the company the person physically works at, because that is how somebody
    /// reads it: "of the people on my books, where are they standing". The «Fərq» column is the point
    /// of the file — it is the only place the two answers are printed side by side.
    /// </summary>
    public static byte[] Build(IReadOnlyList<Row> rows, string? employer, bool onlyElsewhere)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Sənəd üzrə");
        var cols = Headers.Length;

        ws.Cell(1, 1).Value = string.IsNullOrWhiteSpace(employer)
            ? "Sənəd üzrə siyahı — bütün qrup"
            : $"Sənəd üzrə siyahı — {employer}";
        ws.Range(1, 1, 1, cols).Merge();
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;

        // Says out loud what the file is, so nobody reads it as an attendance report. It is a roster:
        // who belongs to whom on paper, and where they actually stand.
        ws.Cell(2, 1).Value = onlyElsewhere
            ? "Yalnız sənədi başqa şirkəti göstərən işçilər. Davamiyyət deyil — kadr siyahısıdır."
            : "Sənədə görə bu şirkətin işçiləri, faktiki iş yerləri ilə birlikdə. Davamiyyət deyil — kadr siyahısıdır.";
        ws.Range(2, 1, 2, cols).Merge();
        ws.Cell(2, 1).Style.Font.Italic = true;
        ws.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#555555");

        const int headerRow = 4;
        for (var i = 0; i < cols; i++)
            ws.Cell(headerRow, i + 1).Value = Headers[i];

        var head = ws.Range(headerRow, 1, headerRow, cols);
        head.Style.Font.Bold = true;
        head.Style.Fill.BackgroundColor = XLColor.FromHtml("#1E70C8");
        head.Style.Font.FontColor = XLColor.White;
        head.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        head.Style.Alignment.WrapText = true;

        var r = headerRow + 1;
        foreach (var p in rows)
        {
            ws.Cell(r, 1).Value = p.FullName;
            ws.Cell(r, 2).Value = p.Position ?? string.Empty;
            ws.Cell(r, 3).Value = p.PaperEmployer;
            ws.Cell(r, 4).Value = p.PaperSite ?? string.Empty;
            ws.Cell(r, 5).Value = p.ActualCompany;
            ws.Cell(r, 6).Value = p.ActualSite;
            // A leading zero, the way people read it back to each other; Excel would eat it from a number.
            ws.Cell(r, 7).Value = string.IsNullOrWhiteSpace(p.PhoneNumber) ? string.Empty : $"0{p.PhoneNumber}";
            ws.Cell(r, 8).Value = p.IsActive ? "Aktiv" : "Deaktiv";
            ws.Cell(r, 9).Value = p.Elsewhere ? "Başqa şirkətdə" : string.Empty;
            if (p.Elsewhere)
            {
                var band = ws.Range(r, 1, r, cols);
                band.Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF4CE");
                band.Style.Font.Bold = true;
            }
            r++;
        }

        if (rows.Count > 0)
        {
            var table = ws.Range(headerRow, 1, r - 1, cols);
            table.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            table.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        }
        else
        {
            ws.Cell(headerRow + 1, 1).Value = "Bu şərtlərə uyğun işçi yoxdur.";
            ws.Range(headerRow + 1, 1, headerRow + 1, cols).Merge();
        }

        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(headerRow);

        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        return stream.ToArray();
    }
}
