using AttendanceQR.Api;
using ClosedXML.Excel;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The «sənəd üzrə» workbook — the roster one employer's documents claim, wherever those people
/// stand.
///
/// It exists because no company panel can produce it: a person on Bakı Abadlıq's books may have
/// their account in Green Garden's data, and one company may not read another's rows. What these
/// tests hold is the thing the file is FOR — the two answers printed side by side, and the borrowed
/// rows made impossible to miss.
/// </summary>
public class PaperRosterSheetTests
{
    private static PaperRosterSheet.Row Person(
        string name, string paperEmployer, string actualCompany,
        string? paperSite = null, string actualSite = "Mərkəz", string? phone = null, bool active = true)
        => new(name, "Bağban", paperEmployer, paperSite, actualCompany, actualSite, phone, active,
            !string.Equals(paperEmployer, actualCompany, StringComparison.OrdinalIgnoreCase));

    private static IXLWorksheet Sheet(params PaperRosterSheet.Row[] rows)
        => new XLWorkbook(new MemoryStream(PaperRosterSheet.Build(rows, "Bakı Abadlıq Xidməti", false)))
            .Worksheet("Sənəd üzrə");

    private static int Col(IXLWorksheet ws, string header)
    {
        for (var c = 1; c <= 20; c++)
            if (ws.Cell(4, c).GetString() == header) return c;
        throw new Xunit.Sdk.XunitException($"«{header}» sütunu tapılmadı.");
    }

    private static int RowOf(IXLWorksheet ws, string name)
    {
        for (var r = 5; r <= 60; r++)
            if (ws.Cell(r, 1).GetString() == name) return r;
        throw new Xunit.Sdk.XunitException($"«{name}» sətri tapılmadı.");
    }

    [Fact]
    public void Both_answers_are_printed_side_by_side()
    {
        // The whole point of the file. Çingiz works at Green Garden and is on Bakı Abadlıq's books;
        // no tenant panel can put those two facts on one line, and this one has to.
        var ws = Sheet(Person(
            "Çingiz Hümbətov", "Bakı Abadlıq Xidməti", "Green Garden",
            paperSite: "Nərimanov Ofis", actualSite: "Green Garden"));
        var r = RowOf(ws, "Çingiz Hümbətov");

        Assert.Equal("Bakı Abadlıq Xidməti", ws.Cell(r, Col(ws, "Sənəd üzrə şirkət")).GetString());
        Assert.Equal("Nərimanov Ofis", ws.Cell(r, Col(ws, "Sənəd üzrə ərazi")).GetString());
        Assert.Equal("Green Garden", ws.Cell(r, Col(ws, "Faktiki şirkət")).GetString());
        Assert.Equal("Green Garden", ws.Cell(r, Col(ws, "Faktiki ərazi")).GetString());
    }

    [Fact]
    public void A_borrowed_row_is_marked_and_banded_and_an_ordinary_one_is_not()
    {
        // Marked in a column AND in colour: this file is read by somebody scanning for the exceptions
        // in a list of hundreds, and a lone word in the ninth column is not findable.
        var ws = Sheet(
            Person("Kənardan", "Bakı Abadlıq Xidməti", "CleanFix"),
            Person("Öz adamı", "Bakı Abadlıq Xidməti", "Bakı Abadlıq Xidməti"));

        var fərq = Col(ws, "Fərq");
        var borrowed = RowOf(ws, "Kənardan");
        var own = RowOf(ws, "Öz adamı");

        Assert.Equal("Başqa şirkətdə", ws.Cell(borrowed, fərq).GetString());
        Assert.Equal(string.Empty, ws.Cell(own, fərq).GetString());
        Assert.NotEqual(
            ws.Cell(own, 1).Style.Fill.BackgroundColor,
            ws.Cell(borrowed, 1).Style.Fill.BackgroundColor);
    }

    [Fact]
    public void The_phone_keeps_its_leading_zero()
    {
        // Written as text on purpose: as a number Excel eats the 0 and 051… becomes 51…, which is not
        // a number anybody can dial back.
        var ws = Sheet(Person("Kimsə", "Bakı Abadlıq Xidməti", "Green Garden", phone: "512409767"));

        Assert.Equal("0512409767", ws.Cell(RowOf(ws, "Kimsə"), Col(ws, "Telefon")).GetString());
    }

    [Fact]
    public void An_empty_result_says_so_instead_of_handing_back_a_bare_grid()
    {
        var wb = new XLWorkbook(new MemoryStream(PaperRosterSheet.Build([], "CleanFix", true)));
        var ws = wb.Worksheet("Sənəd üzrə");

        Assert.Contains("işçi yoxdur", ws.Cell(5, 1).GetString());
    }

    [Fact]
    public void The_title_names_the_employer_and_the_note_says_it_is_not_attendance()
    {
        // It is a roster, not a report of who came in. Somebody receiving it with no covering message
        // must not read it as attendance and conclude the whole company was absent.
        var wb = new XLWorkbook(new MemoryStream(PaperRosterSheet.Build(
            [Person("Kimsə", "CleanFix", "CleanFix")], "CleanFix", false)));
        var ws = wb.Worksheet("Sənəd üzrə");

        Assert.Contains("CleanFix", ws.Cell(1, 1).GetString());
        Assert.Contains("Davamiyyət deyil", ws.Cell(2, 1).GetString());
    }

    [Fact]
    public void Without_an_employer_the_title_says_the_whole_group()
    {
        var wb = new XLWorkbook(new MemoryStream(PaperRosterSheet.Build(
            [Person("Kimsə", "CleanFix", "CleanFix")], null, false)));

        Assert.Contains("bütün qrup", wb.Worksheet("Sənəd üzrə").Cell(1, 1).GetString());
    }
}
