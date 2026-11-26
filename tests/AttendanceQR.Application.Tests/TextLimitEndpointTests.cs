using System.Security.Claims;
using System.Text.Json;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Application.Common;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Security;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Where people type text or money that ends up in a limited column, the screen is told which field and its limit —
/// before the save, so nothing is half-written and the person knows the fix is to shorten it. Each of these was an
/// empty 500 on Postgres and a pass on the in-memory test database.
/// </summary>
public class TextLimitEndpointTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000f2");

    private static AppDbContext NewDb(IInterceptor? interceptor = null)
    {
        var tenant = new TenantContext();
        tenant.Resolve(TenantId);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"limits-api-{Guid.NewGuid()}");
        if (interceptor is not null)
            options.AddInterceptors(interceptor);
        var db = new AppDbContext(options.Options, tenant);
        db.Tenants.Add(new Tenant { Id = TenantId, Name = "T", Slug = "t", DisplayName = "T", IsActive = true });
        db.SaveChanges();
        return db;
    }

    private static T WithContext<T>(T controller, Guid? who = null) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim("sub", (who ?? Guid.NewGuid()).ToString()),
                    new Claim("role", nameof(EmployeeRole.Admin)),
                ], "test")),
            },
        };
        return controller;
    }

    private static JsonElement Body(IActionResult result) =>
        JsonSerializer.SerializeToElement(Assert.IsAssignableFrom<ObjectResult>(result).Value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static void AssertTooLong(IActionResult result, string field, int max)
    {
        Assert.IsType<BadRequestObjectResult>(result);
        var body = Body(result);
        Assert.Equal("TextTooLong", body.GetProperty("error").GetString());
        Assert.Equal(field, body.GetProperty("field").GetString());
        Assert.Equal(max, body.GetProperty("max").GetInt32());
    }

    private sealed class NoSummary : IDailySummaryService
    {
        public Task<int> GenerateForDateAsync(DateOnly date, CancellationToken ct = default) => Task.FromResult(0);
    }

    // --- «Qeyri-iş günü» ------------------------------------------------------------------------------------

    [Fact]
    public async Task A_holiday_description_longer_than_its_column_is_refused_with_the_limit_and_nothing_is_saved()
    {
        using var db = NewDb();
        var max = FieldLimits.MaxLength<NonWorkingDay>(db, nameof(NonWorkingDay.Description))!.Value;
        var calendar = WithContext(new AdminCalendarController(db, new NoSummary()));

        // Trimmed first: surrounding spaces do not count against the limit.
        var fits = await calendar.Create(new NonWorkingDayRequest(new DateOnly(2026, 3, 20), "  " + new string('ə', max) + "  ", null));
        Assert.IsType<OkObjectResult>(fits);

        var tooLong = await calendar.Create(new NonWorkingDayRequest(new DateOnly(2026, 3, 21), new string('ə', max + 1), null));
        AssertTooLong(tooLong, "description", max);
        Assert.Single(db.NonWorkingDays);
        Assert.All(db.NonWorkingDays, d => Assert.True(d.Description.Length <= max));
    }

    // --- the equipment register -----------------------------------------------------------------------------

    private static IFormFile Register(params (int No, string Name, string Area, string Kit)[] lines)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Siyahı");
        ws.Cell(1, 1).Value = "Sıra №";
        ws.Cell(1, 2).Value = "Soyadı, adı, atasının adı";
        ws.Cell(1, 3).Value = "İşlədiyi ərazi";
        ws.Cell(1, 4).Value = "Avadanlıq";
        for (var i = 0; i < lines.Length; i++)
        {
            ws.Cell(i + 2, 1).Value = lines[i].No;
            ws.Cell(i + 2, 2).Value = lines[i].Name;
            ws.Cell(i + 2, 3).Value = lines[i].Area;
            ws.Cell(i + 2, 4).Value = lines[i].Kit;
        }
        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return new FormFile(ms, 0, ms.Length, "file", "register.xlsx");
    }

    [Fact]
    public async Task One_over_long_cell_no_longer_fails_the_whole_import_it_is_named_and_the_rest_goes_in()
    {
        using var db = NewDb();
        var nameMax = FieldLimits.MaxLength<EquipmentRecord>(db, nameof(EquipmentRecord.FullName))!.Value;
        var kitMax = FieldLimits.MaxLength<EquipmentRecord>(db, nameof(EquipmentRecord.Equipment))!.Value;
        var controller = WithContext(new AdminEquipmentController(db));

        // A note typed under the table in the name column, and a kit cell four times the size anyone writes.
        var note = "Qeyd: " + new string('x', nameMax);
        var result = await controller.Import(Register(
            (1, "Birinci İşçi", "Mərkəzi ofis", "1 ədəd noutbuk"),
            (2, "İkinci İşçi", "Anbar", new string('k', kitMax + 1)),
            (3, "Üçüncü İşçi", "Anbar", "2 ədəd monitor"),
            (4, note, "", "-")));

        Assert.IsType<OkObjectResult>(result);
        var body = Body(result);
        Assert.Equal(2, body.GetProperty("added").GetInt32());
        var refused = body.GetProperty("tooLong").EnumerateArray().ToList();
        Assert.Equal(2, refused.Count);
        Assert.Equal(2, refused[0].GetProperty("rowNo").GetInt32());
        Assert.Equal("equipment", refused[0].GetProperty("field").GetString());
        Assert.Equal(kitMax, refused[0].GetProperty("max").GetInt32());
        Assert.Equal(4, refused[1].GetProperty("rowNo").GetInt32());
        Assert.Equal("fullName", refused[1].GetProperty("field").GetString());
        // The over-long name is shortened for the report, never sent back whole.
        Assert.True(refused[1].GetProperty("fullName").GetString()!.Length <= 81);

        Assert.Equal(new[] { "Birinci İşçi", "Üçüncü İşçi" }, db.EquipmentRecords.OrderBy(r => r.RowNo).Select(r => r.FullName).ToList());
    }

    [Fact]
    public async Task An_over_long_cell_in_an_existing_line_leaves_that_line_as_it_was()
    {
        using var db = NewDb();
        var kitMax = FieldLimits.MaxLength<EquipmentRecord>(db, nameof(EquipmentRecord.Equipment))!.Value;
        db.EquipmentRecords.Add(new EquipmentRecord { RowNo = 1, FullName = "Birinci İşçi", Equipment = "1 ədəd noutbuk" });
        db.SaveChanges();
        var controller = WithContext(new AdminEquipmentController(db));

        var result = await controller.Import(Register((1, "Birinci İşçi", "", new string('k', kitMax + 1))));

        Assert.Equal(0, Body(result).GetProperty("updated").GetInt32());
        Assert.Equal("1 ədəd noutbuk", db.EquipmentRecords.AsNoTracking().Single().Equipment);
    }

    [Fact]
    public async Task A_typed_register_line_longer_than_a_column_is_refused_with_the_field_and_its_limit()
    {
        using var db = NewDb();
        var max = FieldLimits.MaxLength<EquipmentRecord>(db, nameof(EquipmentRecord.Position))!.Value;
        var controller = WithContext(new AdminEquipmentController(db));
        EquipmentRecordRequest Line(string? position) =>
            new(null, "Birinci İşçi", position, null, "1 ədəd noutbuk", null, null, null, null);

        AssertTooLong(await controller.Create(Line(new string('v', max + 1))), "position", max);
        Assert.Empty(db.EquipmentRecords);

        Assert.IsType<OkObjectResult>(await controller.Create(Line("Mühəndis")));
        var id = db.EquipmentRecords.Single().Id;
        AssertTooLong(await controller.Update(id, Line(new string('v', max + 1))), "position", max);
        Assert.Equal("Mühəndis", db.EquipmentRecords.AsNoTracking().Single().Position);
    }

    // --- operator money -------------------------------------------------------------------------------------

    private sealed class Hasher : IPasswordHasher
    {
        public string Hash(string password) => "hashed:" + password;
        public bool Verify(string hash, string password) => hash == "hashed:" + password;
    }

    private sealed class Jwt : IJwtService
    {
        public string GenerateToken(Employee employee) => "token";
        public string GenerateImpersonationToken(Employee employee, Guid by, bool readOnly = false) => "imp";
    }

    /// <summary>The operator console as an allowlisted operator, over the one company of <see cref="NewDb"/>.</summary>
    private static SuperAdminController Operator(AppDbContext db)
    {
        var operatorId = Guid.NewGuid();
        var tenant = new TenantContext();
        tenant.Resolve(TenantId);
        return WithContext(new SuperAdminController(db, tenant, new Hasher(), new Jwt(),
            new AppOptions { SuperAdminEmployeeIds = operatorId.ToString() }), operatorId);
    }

    private static void AssertTooLarge(IActionResult result, string field)
    {
        Assert.IsType<BadRequestObjectResult>(result);
        var body = Body(result);
        Assert.Equal("NumberTooLarge", body.GetProperty("error").GetString());
        Assert.Equal(field, body.GetProperty("field").GetString());
        Assert.Equal(99_999_999.99m, body.GetProperty("max").GetDecimal());
    }

    [Fact]
    public async Task An_invoice_amount_too_large_for_its_column_is_refused_and_no_bill_is_written()
    {
        using var db = NewDb();
        var console = Operator(db);

        AssertTooLarge(await console.MarkBilling(TenantId, new BillingMarkRequest(2026, 11, true, 100_000_000m)), "amount");
        Assert.Empty(db.TenantInvoices);
    }

    [Fact]
    public async Task An_invoice_amount_is_stored_and_answered_as_the_column_rounds_it()
    {
        using var db = NewDb();
        var result = await Operator(db).MarkBilling(TenantId, new BillingMarkRequest(2026, 11, true, 1250.505m));

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1250.51m, Body(result).GetProperty("amount").GetDecimal());
        Assert.Equal(1250.51m, db.TenantInvoices.Single().Amount);
    }

    /// <summary>The database refusing the new invoice for a reason that is NOT a lost race.</summary>
    private sealed class RefuseInvoices : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<TenantInvoice>().Any(e => e.State == EntityState.Added))
                throw new DbUpdateException("simulated: the database refused the invoice, not on a unique index");
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task A_refusal_that_is_not_a_unique_violation_is_not_taken_for_a_lost_race()
    {
        // The catch used to take every DbUpdateException for a lost race, re-read a row that did not exist and throw
        // «Sequence contains no elements» — a 500 whose log pointed at a race that never happened. Now the real
        // failure is what surfaces.
        using var db = NewDb(new RefuseInvoices());
        var e = await Assert.ThrowsAsync<DbUpdateException>(() =>
            Operator(db).MarkBilling(TenantId, new BillingMarkRequest(2026, 11, true, 100m)));
        Assert.Contains("not on a unique index", e.Message);
    }

    [Fact]
    public async Task A_price_override_too_large_for_its_column_is_refused_and_nothing_of_the_plan_changes()
    {
        using var db = NewDb();
        var result = await Operator(db).SetPlan(TenantId,
            new TenantPlanRequest(Plan: "Start", MaxEmployees: 50, MonthlyPriceOverride: 1_000_000_000m));

        AssertTooLarge(result, "monthlyPriceOverride");
        var tenant = db.Tenants.AsNoTracking().Single();
        Assert.Null(tenant.Plan);
        Assert.Null(tenant.MaxEmployees);
        Assert.Null(tenant.MonthlyPriceOverride);
    }

    [Fact]
    public async Task A_price_override_that_fits_is_saved_as_the_column_rounds_it()
    {
        using var db = NewDb();
        var result = await Operator(db).SetPlan(TenantId, new TenantPlanRequest(MonthlyPriceOverride: 99_999_999.99m));

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(99_999_999.99m, db.Tenants.AsNoTracking().Single().MonthlyPriceOverride);
    }
}
