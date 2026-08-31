using AttendanceQR.Api;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Which line of the register a spreadsheet row belongs to — and who it belongs to.
///
/// These are the two ways the import could quietly corrupt the register, and one of them was live.
///
/// It matched rows on "Sıra №", the line's POSITION in a file somebody maintains by hand. Inserting a
/// row in the middle — the ordinary way to add a new hire — renumbers every line below it, and the
/// import then walked down the file writing each person's name and kit onto the line above theirs,
/// all the way to the bottom. Worse, `EmployeeId` was kept wherever the new name matched nobody, so a
/// row could end up reading "Yeni İşçi", carrying Yeni İşçi's laptops, and still linked to
/// Məmmədov's staff account — one person's equipment showing on another person's profile, with
/// nothing anywhere saying so.
///
/// The other was quieter and total: the staff link was an exact string comparison between two
/// conventions that never agree — «Məmmədov Elçin Rəşid oğlu» in the register, «Elçin Məmmədov» in
/// the staff list. On the live register that matched 0 rows out of 80.
/// </summary>
public class EquipmentImportIdentityTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000e9");

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public AdminEquipmentController Controller { get; }

        public Harness()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase($"eq-import-{Guid.NewGuid()}").Options,
                tenant);

            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "A", Slug = "a", DisplayName = "A", IsActive = true });
            Db.SaveChanges();

            Controller = new AdminEquipmentController(Db)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            };
        }

        public Guid AddEmployee(string fullName)
        {
            var id = Guid.NewGuid();
            Db.Employees.Add(new Employee
            {
                Id = id, TenantId = TenantId, FullName = fullName, Role = EmployeeRole.Employee,
                IsActive = true, PasswordHash = "h",
            });
            Db.SaveChanges();
            return id;
        }

        public void AddRow(int rowNo, string name, string kit, Guid? employeeId = null)
        {
            Db.EquipmentRecords.Add(new EquipmentRecord
            {
                TenantId = TenantId, RowNo = rowNo, FullName = name, Equipment = kit, EmployeeId = employeeId,
            });
            Db.SaveChanges();
        }

        public EquipmentRecord Row(string name)
            => Db.EquipmentRecords.AsNoTracking().Single(r => r.FullName == name);

        public Task<IActionResult> Import(params (int No, string Name, string Kit)[] lines)
            => Controller.Import(File(lines));

        public void Dispose() => Db.Dispose();
    }

    /// <summary>A register spreadsheet with the columns the parser looks for.</summary>
    private static IFormFile File(params (int No, string Name, string Kit)[] lines)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Siyahı");
        ws.Cell(1, 1).Value = "Sıra №";
        ws.Cell(1, 2).Value = "Soyadı, adı, atasının adı";
        ws.Cell(1, 3).Value = "Avadanlıq";
        for (var i = 0; i < lines.Length; i++)
        {
            ws.Cell(i + 2, 1).Value = lines[i].No;
            ws.Cell(i + 2, 2).Value = lines[i].Name;
            ws.Cell(i + 2, 3).Value = lines[i].Kit;
        }

        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return new FormFile(ms, 0, ms.Length, "file", "register.xlsx");
    }

    // --- the row-shift corruption ------------------------------------------------------------

    [Fact]
    public async Task Inserting_a_line_mid_file_does_not_overwrite_the_person_who_was_there()
    {
        // A partial upload — one office's sheet, or the top of the file — with a new hire added at
        // line 2. Under the old rule the row holding Bəşirov WAS row 2, so it was overwritten with
        // Yeni's name and Yeni's kit, and Bəşirov disappeared from the register entirely. Nothing
        // reported it: the import counted an "update".
        using var h = new Harness();
        h.AddRow(1, "Əliyev Anar Vaqif oğlu", "1 ədəd monitor");
        h.AddRow(2, "Bəşirov Bəhruz Elmar oğlu", "1 ədəd noutbuk");
        h.AddRow(3, "Cəfərov Ceyhun Nadir oğlu", "1 ədəd printer");

        await h.Import(
            (1, "Əliyev Anar Vaqif oğlu", "1 ədəd monitor"),
            (2, "Yeni İşçi Yeni oğlu", "1 ədəd skaner"));

        Assert.Equal("1 ədəd noutbuk", h.Row("Bəşirov Bəhruz Elmar oğlu").Equipment);
        Assert.Equal("1 ədəd skaner", h.Row("Yeni İşçi Yeni oğlu").Equipment);
        Assert.Equal(4, h.Db.EquipmentRecords.Count());
    }

    [Fact]
    public async Task A_renumbered_person_stays_the_same_row_of_the_table()
    {
        // The full-file case, and the reason it needs looking at by IDENTITY rather than by contents.
        // Re-importing the whole file after an insert leaves the register LOOKING right — every name
        // is present with the right kit — while every person below the insertion point has been moved
        // onto the row that used to belong to the person above them. Nothing in the data shows it.
        // What shows it is the row id, and what it costs is the staff link the next test is about.
        using var h = new Harness();
        h.AddRow(1, "Əliyev Anar Vaqif oğlu", "1 ədəd monitor");
        h.AddRow(2, "Bəşirov Bəhruz Elmar oğlu", "1 ədəd noutbuk");
        h.AddRow(3, "Cəfərov Ceyhun Nadir oğlu", "1 ədəd printer");
        var wasBashirov = h.Row("Bəşirov Bəhruz Elmar oğlu").Id;
        var wasJafarov = h.Row("Cəfərov Ceyhun Nadir oğlu").Id;

        await h.Import(
            (1, "Əliyev Anar Vaqif oğlu", "1 ədəd monitor"),
            (2, "Yeni İşçi Yeni oğlu", "1 ədəd skaner"),
            (3, "Bəşirov Bəhruz Elmar oğlu", "1 ədəd noutbuk"),
            (4, "Cəfərov Ceyhun Nadir oğlu", "1 ədəd printer"));

        Assert.Equal(wasBashirov, h.Row("Bəşirov Bəhruz Elmar oğlu").Id);
        Assert.Equal(wasJafarov, h.Row("Cəfərov Ceyhun Nadir oğlu").Id);
        Assert.Equal(4, h.Db.EquipmentRecords.Count());
    }

    [Fact]
    public async Task A_renumbered_line_keeps_its_staff_link_and_does_not_lend_it_out()
    {
        // The part that reached a person's profile. The new name matches nobody in the staff list, so
        // the old code's `employeeId ?? record.EmployeeId` left Bəşirov's account attached to a row
        // that now described somebody else entirely.
        using var h = new Harness();
        var bashirov = h.AddEmployee("Bəhruz Bəşirov");
        h.AddRow(1, "Əliyev Anar Vaqif oğlu", "1 ədəd monitor");
        h.AddRow(2, "Bəşirov Bəhruz Elmar oğlu", "1 ədəd noutbuk", bashirov);

        await h.Import(
            (1, "Əliyev Anar Vaqif oğlu", "1 ədəd monitor"),
            (2, "Yeni İşçi Yeni oğlu", "1 ədəd skaner"),
            (3, "Bəşirov Bəhruz Elmar oğlu", "1 ədəd noutbuk"));

        Assert.Equal(bashirov, h.Row("Bəşirov Bəhruz Elmar oğlu").EmployeeId);
        Assert.Null(h.Row("Yeni İşçi Yeni oğlu").EmployeeId);
    }

    [Fact]
    public async Task Re_uploading_the_same_file_changes_nothing()
    {
        // The promise the whole import rests on. Matching by name has to keep it, or the register
        // doubles every time somebody uploads.
        using var h = new Harness();
        await h.Import((1, "Əliyev Anar Vaqif oğlu", "1 ədəd monitor"), (2, "Bəşirov Bəhruz Elmar oğlu", "1 ədəd noutbuk"));
        await h.Import((1, "Əliyev Anar Vaqif oğlu", "1 ədəd monitor"), (2, "Bəşirov Bəhruz Elmar oğlu", "1 ədəd noutbuk"));

        Assert.Equal(2, h.Db.EquipmentRecords.Count());
    }

    [Fact]
    public async Task The_row_number_still_follows_the_file()
    {
        // It stops being the identity but stays the order things are shown in, so it has to track
        // the file rather than being frozen at whatever it was first imported as.
        using var h = new Harness();
        h.AddRow(2, "Bəşirov Bəhruz Elmar oğlu", "1 ədəd noutbuk");

        await h.Import((7, "Bəşirov Bəhruz Elmar oğlu", "1 ədəd noutbuk"));

        Assert.Equal(7, h.Row("Bəşirov Bəhruz Elmar oğlu").RowNo);
    }

    [Fact]
    public async Task Two_people_with_the_same_name_are_kept_apart_by_their_line_number()
    {
        // Name alone cannot separate them, so the pair (name, number) does. Without this the second
        // line would overwrite the first and one of the two would vanish from the register.
        using var h = new Harness();
        h.AddRow(1, "Məmmədov Elçin", "1 ədəd monitor");
        h.AddRow(2, "Məmmədov Elçin", "1 ədəd noutbuk");

        await h.Import((1, "Məmmədov Elçin", "1 ədəd monitor"), (2, "Məmmədov Elçin", "1 ədəd noutbuk"));

        Assert.Equal(2, h.Db.EquipmentRecords.Count());
        Assert.Equal("1 ədəd monitor", h.Db.EquipmentRecords.Single(r => r.RowNo == 1).Equipment);
        Assert.Equal("1 ədəd noutbuk", h.Db.EquipmentRecords.Single(r => r.RowNo == 2).Equipment);
    }

    [Fact]
    public async Task A_line_the_file_no_longer_has_is_left_alone()
    {
        // Deliberate, and unchanged: a partial file — one office's sheet — must not be able to wipe
        // the rest of the register.
        using var h = new Harness();
        h.AddRow(1, "Əliyev Anar Vaqif oğlu", "1 ədəd monitor");
        h.AddRow(2, "Bəşirov Bəhruz Elmar oğlu", "1 ədəd noutbuk");

        await h.Import((1, "Əliyev Anar Vaqif oğlu", "2 ədəd monitor"));

        Assert.Equal(2, h.Db.EquipmentRecords.Count());
        Assert.Equal("1 ədəd noutbuk", h.Row("Bəşirov Bəhruz Elmar oğlu").Equipment);
    }

    // --- the staff link ----------------------------------------------------------------------

    [Fact]
    public async Task The_register_and_the_staff_list_write_a_name_differently_and_still_match()
    {
        // 0 of 80 on the live register. The two conventions never agree and were compared for
        // equality: surname-first with a patronymic against however the account was typed.
        using var h = new Harness();
        var id = h.AddEmployee("Elçin Məmmədov");

        await h.Import((1, "Məmmədov Elçin Rəşid oğlu", "1 ədəd monitor"));

        Assert.Equal(id, h.Row("Məmmədov Elçin Rəşid oğlu").EmployeeId);
    }

    [Fact]
    public async Task Two_people_it_could_mean_means_no_link()
    {
        // A wrong link hangs one person's laptops on another and nobody goes looking for it, so an
        // ambiguous name gets no answer rather than a guess.
        using var h = new Harness();
        h.AddEmployee("Elçin Məmmədov");
        h.AddEmployee("Elçin Rəşid Məmmədov");

        await h.Import((1, "Məmmədov Elçin Rəşid oğlu", "1 ədəd monitor"));

        Assert.Null(h.Row("Məmmədov Elçin Rəşid oğlu").EmployeeId);
    }

    [Fact]
    public async Task A_surname_on_its_own_links_to_nobody()
    {
        using var h = new Harness();
        h.AddEmployee("Məmmədov");

        await h.Import((1, "Məmmədov Elçin Rəşid oğlu", "1 ədəd monitor"));

        Assert.Null(h.Row("Məmmədov Elçin Rəşid oğlu").EmployeeId);
    }

    [Fact]
    public async Task The_import_reports_what_it_could_not_place()
    {
        using var h = new Harness();
        h.AddEmployee("Elçin Məmmədov");

        var result = await h.Import(
            (1, "Məmmədov Elçin Rəşid oğlu", "1 ədəd monitor"),
            (2, "Tanınmayan Adam Adil oğlu", "1 ədəd noutbuk"));

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, ok.Value!.GetType().GetProperty("linked")!.GetValue(ok.Value));
        var unmatched = (List<string>)ok.Value.GetType().GetProperty("unmatched")!.GetValue(ok.Value)!;
        Assert.Equal(["Tanınmayan Adam Adil oğlu"], unmatched);
    }
}
