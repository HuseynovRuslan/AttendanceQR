using System.Security.Claims;
using System.Text.Json;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Domain.Enums;
using AttendanceQR.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit.Abstractions;

namespace AttendanceQR.IntegrationTests.Tenancy;

/// <summary>
/// SEC-01 — a company's billing screen must show THAT company's plan, price and invoices, never another's.
///
/// The Tenants and TenantInvoices tables carry no query filter (they are the platform's own registry), so the
/// "fail-closed" tenant filter cannot save a query that forgets to say which tenant it means. On PostgreSQL an
/// unordered single-row read returns the first row in heap order — which depends on insertion order and on
/// which rows were updated since. Every scenario below therefore arranges a DIFFERENT physical first row, and in
/// every one of them each company must read only its own figures.
///
/// Runs the controller against a real database; the HTTP twin of this test is BillingIsolationHttpTests.
/// </summary>
public class BillingIsolationDbTests
{
    private readonly ITestOutputHelper _out;
    public BillingIsolationDbTests(ITestOutputHelper output) => _out = output;

    public enum Arrangement
    {
        /// <summary>Original tenant (from the migration) first, then A, then B.</summary>
        OriginalThenAThenB,
        /// <summary>Original first, then B, then A — the reverse creation order of the two new companies.</summary>
        OriginalThenBThenA,
        /// <summary>The original tenant's row is updated, which moves it to the end of the heap: A is first.</summary>
        OriginalRowRewritten,
    }

    private sealed record Company(string Name, Guid Id, decimal Price, string? Plan, string[] InvoiceNotes, Guid LocationId, Guid AdminId);

    [PostgresTheory]
    [InlineData(Arrangement.OriginalThenAThenB)]
    [InlineData(Arrangement.OriginalThenBThenA)]
    [InlineData(Arrangement.OriginalRowRewritten)]
    public async Task Every_company_reads_only_its_own_plan_price_and_invoices(Arrangement arrangement)
    {
        var cs = await PostgresServer.CreateDatabaseAsync();
        var companies = await ArrangeAsync(cs, arrangement);

        var first = await Seed.PhysicallyFirstTenantAsync(cs);
        _out.WriteLine($"{arrangement}: physically first Tenants row = {companies.Single(c => c.Id == first).Name}");

        foreach (var company in companies)
        {
            var body = await LoadBillingAsync(cs, company);
            var monthly = body.GetProperty("monthly");

            Assert.True(monthly.GetProperty("amount").GetDecimal() == company.Price,
                $"{company.Name} admin was shown amount {monthly.GetProperty("amount")} — its own price is {company.Price}");
            Assert.True(monthly.GetProperty("isNegotiated").GetBoolean(), $"{company.Name}: isNegotiated");

            var plan = body.GetProperty("plan");
            Assert.Equal(company.Plan, plan.ValueKind == JsonValueKind.Null ? null : plan.GetString());

            var notes = body.GetProperty("invoices").EnumerateArray().Select(i => i.GetProperty("note").GetString()).ToArray();
            Assert.True(notes.OrderBy(n => n).SequenceEqual(company.InvoiceNotes.OrderBy(n => n)),
                $"{company.Name} admin was shown invoices [{string.Join(", ", notes)}] — its own are [{string.Join(", ", company.InvoiceNotes)}]");
        }
    }

    private static async Task<List<Company>> ArrangeAsync(string cs, Arrangement arrangement)
    {
        // The original tenant row already exists (the AddTenants migration inserts it). Give it figures that are
        // recognisable if they ever leak.
        var original = Seed.OriginalTenantId;
        await Seed.SetBillingAsync(cs, original, 999.00m, "Enterprise");
        await Seed.InvoiceAsync(cs, original, 2026, 10, 999.00m, 1004, "ORIGINAL-SECRET-NOTE");

        Guid a, b;
        if (arrangement == Arrangement.OriginalThenBThenA)
        {
            b = await Seed.TenantAsync(cs, "betaco", 222.00m, "Biznes");
            a = await Seed.TenantAsync(cs, "alfaco", 111.00m, "Start");
        }
        else
        {
            a = await Seed.TenantAsync(cs, "alfaco", 111.00m, "Start");
            b = await Seed.TenantAsync(cs, "betaco", 222.00m, "Biznes");
        }
        // A has an invoice of its own; B has none at all — it must get an EMPTY history, not somebody else's.
        await Seed.InvoiceAsync(cs, a, 2026, 10, 111.00m, 7, "ALFA-NOTE");

        if (arrangement == Arrangement.OriginalRowRewritten)
            await Seed.TouchTenantAsync(cs, original);

        var list = new List<Company>();
        foreach (var (name, id, price, plan, notes) in new[]
                 {
                     ("original", original, 999.00m, (string?)"Enterprise", new[] { "ORIGINAL-SECRET-NOTE" }),
                     ("alfaco", a, 111.00m, "Start", new[] { "ALFA-NOTE" }),
                     ("betaco", b, 222.00m, "Biznes", Array.Empty<string>()),
                 })
        {
            var location = await Seed.LocationAsync(cs, id);
            var admin = await Seed.EmployeeAsync(cs, id, location, $"admin@{name}.test", "4821", EmployeeRole.Admin);
            list.Add(new Company(name, id, price, plan, notes, location, admin));
        }
        return list;
    }

    private static async Task<JsonElement> LoadBillingAsync(string cs, Company company)
    {
        await using var db = Seed.Context(cs, company.Id);
        var controller = new TenantBillingController(db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim("sub", company.AdminId.ToString()),
                        new Claim("role", "Admin"),
                        new Claim("tid", company.Id.ToString()),
                    }, "test")),
                },
            },
        };
        var ok = Assert.IsType<OkObjectResult>(await controller.Mine());
        return JsonSerializer.SerializeToElement(ok.Value);
    }
}
