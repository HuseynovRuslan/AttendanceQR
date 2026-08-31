using AttendanceQR.Api;
using ClosedXML.Excel;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The IT equipment register is imported from a spreadsheet somebody maintains by hand, so the parser
/// is tested against the shapes that file actually takes: a title banner above the header, a blank
/// spacer row, reordered columns, and Azerbaijani headers.
/// </summary>
public class EquipmentSheetTests
{
    /// <summary>Builds a workbook in memory. Each row is a list of (column, text) pairs so a test can
    /// place columns wherever it likes.</summary>
    private static Stream Sheet(params (int Col, string Text)[][] rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Siyahı");
        for (var r = 0; r < rows.Length; r++)
            foreach (var (col, text) in rows[r])
                ws.Cell(r + 1, col).Value = text;

        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    private static (int, string)[] Header() =>
    [
        (1, "Sıra №"), (2, "Soyadı, adı, atasının adı"), (3, "Vəzifəsi"), (4, "İşlədiyi ərazi"),
        (5, "Avadanlıq"), (6, "Sistem bloku"), (7, "Monitor"), (8, "Digər avadanlıq"),
    ];

    [Fact]
    public void Reads_rows_under_a_title_banner()
    {
        using var stream = Sheet(
            [(1, "İT AVADANLIQLARININ SİYAHISI")],
            [],
            Header(),
            [],
            [(1, "1"), (2, "Qasımov Binnət Əli"), (3, "Direktor"), (4, "Dalğa Plaza ofisi"),
             (5, "1 ədəd iMac Pro 27\"")]);

        var (error, rows) = EquipmentSheet.Parse(stream);

        Assert.Equal(EquipmentSheetError.Ok, error);
        var row = Assert.Single(rows);
        Assert.Equal(1, row.RowNo);
        Assert.Equal("Qasımov Binnət Əli", row.FullName);
        Assert.Equal("Direktor", row.Position);
        Assert.Equal("Dalğa Plaza ofisi", row.Area);
        Assert.Equal("1 ədəd iMac Pro 27\"", row.Equipment);
        Assert.Null(row.SystemUnit);
    }

    /// <summary>
    /// "İşlədiyi ərazi" starts with the dotted capital İ, which ToLowerInvariant turns into 'i' plus a
    /// COMBINING DOT ABOVE — not the plain 'i' the header list is written with. Without normalising,
    /// the column goes unmapped and every row imports with a blank area: a silent hole, not an error.
    /// </summary>
    [Fact]
    public void Maps_the_area_column_despite_the_dotted_capital_i()
    {
        using var stream = Sheet(
            Header(),
            [(1, "1"), (2, "Əliyeva Esmira"), (4, "Nərimanov ofisi"), (5, "1 ədəd monoblok HP")]);

        var (_, rows) = EquipmentSheet.Parse(stream);

        Assert.Equal("Nərimanov ofisi", Assert.Single(rows).Area);
    }

    /// <summary>Columns are found by header text, so a reordered file still lands in the right fields.
    /// By position, this sheet's monitor column would import as "digər avadanlıq".</summary>
    [Fact]
    public void Finds_columns_by_header_not_by_position()
    {
        using var stream = Sheet(
            [(1, "Soyadı, adı, atasının adı"), (2, "Avadanlıq"), (3, "Digər avadanlıq"), (4, "Monitor")],
            [(1, "Məmmədov Emin"), (2, "1 ədəd masaüstü"), (3, "Printer Epson L6190"), (4, "HP 27\" × 2")]);

        var (_, rows) = EquipmentSheet.Parse(stream);

        var row = Assert.Single(rows);
        Assert.Equal("HP 27\" × 2", row.Monitor);
        Assert.Equal("Printer Epson L6190", row.OtherEquipment);
    }

    /// <summary>Blank lines between sections are spacing, not people.</summary>
    [Fact]
    public void Skips_rows_without_a_name()
    {
        using var stream = Sheet(
            Header(),
            [(1, "1"), (2, "Birinci İşçi"), (5, "1 ədəd noutbuk")],
            [(1, "2")],
            [(1, "3"), (2, "Üçüncü İşçi"), (5, "1 ədəd monitor")]);

        var (_, rows) = EquipmentSheet.Parse(stream);

        Assert.Equal(2, rows.Count);
        Assert.Equal([1, 3], rows.Select(r => r.RowNo));
    }

    /// <summary>A line added at the bottom without a number is still imported — numbered after the
    /// highest one seen, so it cannot collide with an existing row.</summary>
    [Fact]
    public void Numbers_a_row_that_has_no_number()
    {
        using var stream = Sheet(
            Header(),
            [(1, "7"), (2, "Nömrəli İşçi"), (5, "1 ədəd noutbuk")],
            [(2, "Nömrəsiz İşçi"), (5, "1 ədəd monitor")]);

        var (_, rows) = EquipmentSheet.Parse(stream);

        Assert.Equal([7, 8], rows.Select(r => r.RowNo));
    }

    /// <summary>The equipment columns hold several lines each; the newlines carry the meaning (one
    /// line per machine), so they survive the import.</summary>
    [Fact]
    public void Keeps_the_line_breaks_inside_a_cell()
    {
        using var stream = Sheet(
            Header(),
            [(1, "1"), (2, "Diker Burak"), (5, "1 ədəd noutbuk"),
             (6, "i7 12-ci nəsil / 16 GB\ni5 9-cu nəsil / 16 GB")]);

        var (_, rows) = EquipmentSheet.Parse(stream);

        Assert.Equal("i7 12-ci nəsil / 16 GB\ni5 9-cu nəsil / 16 GB", Assert.Single(rows).SystemUnit);
    }

    /// <summary>A sheet with no recognisable header is refused rather than imported into the wrong
    /// columns — the one outcome that would quietly corrupt the register.</summary>
    [Fact]
    public void Refuses_a_sheet_with_no_header()
    {
        using var stream = Sheet(
            [(1, "Filial"), (2, "Ünvan")],
            [(1, "Nərimanov"), (2, "Bakı")]);

        var (error, rows) = EquipmentSheet.Parse(stream);

        Assert.Equal(EquipmentSheetError.HeaderNotFound, error);
        Assert.Empty(rows);
    }

    /// <summary>A caption reading "Avadanlıq" over a section is not a header row: both the name and
    /// the equipment column have to be present before a row is believed.</summary>
    [Fact]
    public void Does_not_mistake_a_lone_caption_for_the_header()
    {
        using var stream = Sheet(
            [(1, "Avadanlıq")],
            [],
            Header(),
            [(1, "1"), (2, "Həsənov Murad"), (5, "1 ədəd noutbuk ASUS")]);

        var (error, rows) = EquipmentSheet.Parse(stream);

        Assert.Equal(EquipmentSheetError.Ok, error);
        Assert.Equal("Həsənov Murad", Assert.Single(rows).FullName);
    }
}
