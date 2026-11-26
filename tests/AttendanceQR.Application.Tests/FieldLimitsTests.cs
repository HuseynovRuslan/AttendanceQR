using System.Text;
using System.Text.Json;
using AttendanceQR.Api;
using AttendanceQR.Domain;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The column limits, checked before the save — the net under every place that writes text or money.
///
/// The in-memory test database stores a string of any length and a decimal of any size; Postgres refuses both, as
/// 22001 and 22003. So a value that did not fit passed every test here and failed only in production, as an empty 500.
/// That is how the role-change audit row (PR #69) refused every role change with a green suite. These tests pin that
/// the in-memory database now refuses what Postgres would, and that the API turns it into a 400 that names the field.
/// </summary>
public class FieldLimitsTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000f1");

    private static AppDbContext NewDb()
    {
        var tenant = new TenantContext();
        tenant.Resolve(TenantId);
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"limits-{Guid.NewGuid()}").Options, tenant);
        db.Tenants.Add(new Tenant { Id = TenantId, Name = "T", Slug = "t", DisplayName = "T", IsActive = true });
        db.SaveChanges();
        return db;
    }

    private static int MaxOf<TEntity>(AppDbContext db, string property) =>
        FieldLimits.MaxLength<TEntity>(db, property) ?? throw new InvalidOperationException($"{property} has no limit");

    // --- the guard in SaveChanges ---------------------------------------------------------------------------

    [Fact]
    public async Task A_text_longer_than_its_column_is_refused_by_the_in_memory_database_too()
    {
        using var db = NewDb();
        var max = MaxOf<NonWorkingDay>(db, nameof(NonWorkingDay.Description));
        var secret = "Nazirlər Kabinetinin qərarı " + new string('x', max);
        db.NonWorkingDays.Add(new NonWorkingDay { Date = new DateOnly(2026, 3, 20), Description = secret });

        var e = await Assert.ThrowsAsync<FieldTooLongException>(() => db.SaveChangesAsync());
        Assert.Equal(nameof(NonWorkingDay), e.Entity);
        Assert.Equal(nameof(NonWorkingDay.Description), e.Property);
        Assert.Equal(max, e.MaxLength);
        // The message goes to the logs. It names the column, never what somebody typed into it.
        Assert.DoesNotContain("Nazirlər", e.Message);
        Assert.Contains($"{max}", e.Message);

        // And it IS a DbUpdateException — what Postgres would have thrown — so every catch written for that still
        // sees what it saw before (the best-effort writes after a check-in, the unique-index races).
        Assert.IsAssignableFrom<DbUpdateException>(e);
        Assert.Throws<FieldTooLongException>(() => db.SaveChanges());
    }

    [Fact]
    public void A_text_exactly_at_the_limit_is_saved()
    {
        using var db = NewDb();
        var max = MaxOf<NonWorkingDay>(db, nameof(NonWorkingDay.Description));
        db.NonWorkingDays.Add(new NonWorkingDay { Date = new DateOnly(2026, 3, 20), Description = new string('ə', max) });
        db.SaveChanges();
        Assert.Single(db.NonWorkingDays);
    }

    [Fact]
    public void A_row_changed_to_text_that_does_not_fit_is_refused_and_an_unchanged_column_is_not_looked_at()
    {
        using var db = NewDb();
        var day = new NonWorkingDay { Date = new DateOnly(2026, 3, 20), Description = "Novruz bayramı" };
        db.NonWorkingDays.Add(day);
        db.SaveChanges();

        // A change to another column of the row: the UPDATE does not send Description, so it is not checked.
        day.Date = new DateOnly(2026, 3, 21);
        db.SaveChanges();

        day.Description = new string('x', MaxOf<NonWorkingDay>(db, nameof(NonWorkingDay.Description)) + 1);
        Assert.Throws<FieldTooLongException>(() => db.SaveChanges());
    }

    [Theory]
    [InlineData("99999999.99", false)]
    [InlineData("100000000", true)]
    [InlineData("99999999.995", true)]   // numeric(10,2) rounds this up to 100000000.00 first
    [InlineData("99999999.994", false)]
    [InlineData("-100000000", true)]
    public void Money_larger_than_numeric_10_2_is_refused(string amount, bool refused)
    {
        using var db = NewDb();
        var tenant = db.Tenants.Single();
        tenant.MonthlyPriceOverride = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture);

        if (!refused)
        {
            db.SaveChanges();
            return;
        }
        var e = Assert.Throws<NumberTooLargeException>(() => db.SaveChanges());
        Assert.Equal(nameof(Tenant.MonthlyPriceOverride), e.Property);
        Assert.Equal(10, e.Precision);
        Assert.Equal(2, e.Scale);
        Assert.Equal(99_999_999.99m, e.Max);
    }

    [Fact]
    public void A_decimal_with_no_precision_set_has_no_limit()
    {
        using var db = NewDb();
        db.Employees.Add(new Employee
        {
            FullName = "Aysel Məmmədova", PasswordHash = "h", IsActive = true, MonthlySalary = 1_000_000_000_000m,
        });
        db.SaveChanges();
    }

    [Fact]
    public void Every_limit_in_the_model_is_one_the_guard_knows()
    {
        // The guard reads the same model the migrations were made from. If a limited column ever came with a value
        // converter the guard would skip it silently — say so here instead.
        using var db = NewDb();
        var converted = db.Model.GetEntityTypes()
            .SelectMany(t => t.GetProperties())
            .Where(p => (p.GetMaxLength() is not null || p.GetPrecision() is not null) && p.GetValueConverter() is not null)
            .Select(p => $"{p.DeclaringType.ClrType.Name}.{p.Name}")
            .ToList();
        Assert.Empty(converted);
    }

    // --- the API's answer -----------------------------------------------------------------------------------

    private static ExceptionContext ContextFor(Exception e) =>
        new(new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>())
        {
            Exception = e,
        };

    private static (string? Error, string? Field, string? Max) Read(IActionResult? result)
    {
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var body = JsonSerializer.SerializeToElement(bad.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        string? Prop(string name) => body.TryGetProperty(name, out var v) ? v.ToString() : null;
        return (Prop("error"), Prop("field"), Prop("max"));
    }

    [Fact]
    public async Task The_filter_answers_400_TextTooLong_with_the_field_and_its_limit()
    {
        using var db = NewDb();
        db.NonWorkingDays.Add(new NonWorkingDay { Date = new DateOnly(2026, 3, 20), Description = new string('x', 500) });
        var thrown = await Assert.ThrowsAsync<FieldTooLongException>(() => db.SaveChangesAsync());

        var context = ContextFor(thrown);
        new FieldLimitFilter(NullLogger<FieldLimitFilter>.Instance).OnException(context);

        Assert.True(context.ExceptionHandled);
        Assert.Equal(("TextTooLong", "description", "200"), Read(context.Result));
    }

    [Fact]
    public void The_filter_answers_400_NumberTooLarge_for_money()
    {
        var context = ContextFor(new NumberTooLargeException(nameof(TenantInvoice), nameof(TenantInvoice.Amount), 10, 2, 99_999_999.99m));
        new FieldLimitFilter(NullLogger<FieldLimitFilter>.Instance).OnException(context);
        Assert.Equal(("NumberTooLarge", "amount", "99999999.99"), Read(context.Result));
    }

    [Theory]
    [InlineData(PostgresErrorCodes.StringDataRightTruncation, "TextTooLong")]
    [InlineData(PostgresErrorCodes.NumericValueOutOfRange, "NumberTooLarge")]
    public void The_Postgres_codes_get_the_same_answer_for_a_write_that_bypasses_SaveChanges(string sqlState, string error)
    {
        var pg = new PostgresException("value too long", "ERROR", "ERROR", sqlState);
        var context = ContextFor(new DbUpdateException("save failed", pg));
        new FieldLimitFilter(NullLogger<FieldLimitFilter>.Instance).OnException(context);
        // Postgres does not name the column for these, so there is no field to give.
        Assert.Equal((error, null, null), Read(context.Result));
    }

    [Fact]
    public void Any_other_failure_is_left_alone()
    {
        var context = ContextFor(new DbUpdateException("unique", new PostgresException("dup", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation)));
        new FieldLimitFilter(NullLogger<FieldLimitFilter>.Instance).OnException(context);
        Assert.False(context.ExceptionHandled);
        Assert.Null(context.Result);
    }

    // --- cutting text without breaking a character ----------------------------------------------------------

    /// <summary>The encoder Npgsql writes text with: strict, so half a character throws instead of becoming «?».</summary>
    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    [Fact]
    public void An_emoji_on_the_cut_is_left_out_whole_rather_than_halved()
    {
        var text = new string('a', 199) + "👍" + " və qalanı";
        Assert.Throws<EncoderFallbackException>(() => Strict.GetByteCount(text[..200]));   // what a plain slice did

        var cut = SafeText.Truncate(text, 200);
        Assert.Equal(new string('a', 199), cut);
        Strict.GetByteCount(cut);
    }

    [Theory]
    [InlineData("Novruz", 200, "Novruz")]
    [InlineData("Novruz bayramı", 6, "Novruz")]
    [InlineData("ab👍", 4, "ab👍")]
    [InlineData("ab👍c", 4, "ab👍")]
    [InlineData("👍", 1, "")]
    [InlineData("abc", 0, "")]
    public void Truncate_counts_UTF16_units_and_never_ends_on_half_a_pair(string text, int max, string expected)
    {
        var cut = SafeText.Truncate(text, max);
        Assert.Equal(expected, cut);
        Assert.True(cut.Length <= max);
        Strict.GetByteCount(cut);
    }
}
