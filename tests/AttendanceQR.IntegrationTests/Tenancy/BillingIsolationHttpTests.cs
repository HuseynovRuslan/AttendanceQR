using System.Net;
using System.Text.Json;
using AttendanceQR.Domain.Enums;
using AttendanceQR.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace AttendanceQR.IntegrationTests.Tenancy;

/// <summary>
/// SEC-01 end to end: real sign-in on each company's subdomain, real JWT, real middleware, real PostgreSQL.
/// Proves the billing screen's API answers each company with its own figures whatever order the companies
/// were created in — and that the operator console, which legitimately reads every company, still does.
/// </summary>
public class BillingIsolationHttpTests
{
    private readonly ITestOutputHelper _out;
    public BillingIsolationHttpTests(ITestOutputHelper output) => _out = output;

    private const string Pin = "4821";

    private sealed record Company(string Slug, Guid Id, decimal Price, string Plan, string[] Notes, string AdminEmail);

    [PostgresTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Each_company_admin_sees_only_its_own_billing_over_http(bool betaCreatedFirst, bool originalRowRewritten)
    {
        var cs = await PostgresServer.CreateDatabaseAsync();
        var companies = await ArrangeAsync(cs, betaCreatedFirst, originalRowRewritten, operatorId: null);
        var firstId = await Seed.PhysicallyFirstTenantAsync(cs);
        _out.WriteLine($"betaFirst={betaCreatedFirst} rewritten={originalRowRewritten}: physically first = "
                       + companies.Single(c => c.Id == firstId).Slug);

        await using var factory = new QrLogApiFactory(cs);
        var client = factory.CreateClient();

        foreach (var c in companies)
        {
            var token = await ApiCalls.LoginAsync(client, c.Slug, c.AdminEmail, Pin);
            var (status, body) = await ApiCalls.GetAsync(client, "/api/tenant/billing", token);
            Assert.Equal(HttpStatusCode.OK, status);
            AssertOwnBilling(c, body);

            // The token decides the company, not the Origin header: the same request sent as if from another
            // company's subdomain still answers for the caller's own company.
            foreach (var other in companies.Where(o => o.Id != c.Id))
            {
                var (s2, b2) = await ApiCalls.GetAsync(client, "/api/tenant/billing", token, ApiCalls.OriginFor(other.Slug));
                Assert.Equal(HttpStatusCode.OK, s2);
                AssertOwnBilling(c, b2);
            }
        }
    }

    [PostgresFact]
    public async Task The_operator_console_still_reads_every_company_and_a_company_admin_cannot()
    {
        var cs = await PostgresServer.CreateDatabaseAsync();
        var operatorId = Guid.NewGuid();
        var companies = await ArrangeAsync(cs, betaCreatedFirst: false, originalRowRewritten: false, operatorId);

        await using var factory = new QrLogApiFactory(cs, new[] { operatorId });
        var client = factory.CreateClient();

        // The operator is an allowlisted employee who signs in on their own company's subdomain.
        var operatorToken = await ApiCalls.LoginAsync(client, Seed.OriginalTenantSlug, "operator@platform.test", Pin);
        var (status, body) = await ApiCalls.GetAsync(client, "/api/super/billing?year=2026&month=10", operatorToken);
        Assert.Equal(HttpStatusCode.OK, status);

        var rows = body.GetProperty("rows").EnumerateArray().ToList();
        foreach (var c in companies)
        {
            var row = rows.SingleOrDefault(r => r.GetProperty("tenantId").GetGuid() == c.Id);
            Assert.True(row.ValueKind == JsonValueKind.Object, $"operator billing is missing {c.Slug}");
            Assert.Equal(c.Price, row.GetProperty("priceOverride").GetDecimal());
            Assert.Equal(c.Plan, row.GetProperty("plan").GetString());
        }

        // A company admin is not an operator, whatever their role inside their own company.
        var adminToken = await ApiCalls.LoginAsync(client, companies[1].Slug, companies[1].AdminEmail, Pin);
        var (denied, _) = await ApiCalls.GetAsync(client, "/api/super/billing?year=2026&month=10", adminToken);
        Assert.Equal(HttpStatusCode.Forbidden, denied);
    }

    private static void AssertOwnBilling(Company c, JsonElement body)
    {
        var amount = body.GetProperty("monthly").GetProperty("amount").GetDecimal();
        Assert.True(amount == c.Price, $"{c.Slug} admin was shown {amount}, its own price is {c.Price}");
        Assert.Equal(c.Plan, body.GetProperty("plan").GetString());
        var notes = body.GetProperty("invoices").EnumerateArray().Select(i => i.GetProperty("note").GetString()).OrderBy(n => n);
        Assert.True(notes.SequenceEqual(c.Notes.OrderBy(n => n)),
            $"{c.Slug} admin was shown invoices [{string.Join(", ", notes)}], its own are [{string.Join(", ", c.Notes)}]");
    }

    private static async Task<List<Company>> ArrangeAsync(string cs, bool betaCreatedFirst, bool originalRowRewritten,
        Guid? operatorId)
    {
        await Seed.SetBillingAsync(cs, Seed.OriginalTenantId, 999.00m, "Enterprise");
        await Seed.InvoiceAsync(cs, Seed.OriginalTenantId, 2026, 10, 999.00m, 1004, "ORIGINAL-SECRET-NOTE");

        Guid alfa, beta;
        if (betaCreatedFirst)
        {
            beta = await Seed.TenantAsync(cs, "betaco", 222.00m, "Biznes");
            alfa = await Seed.TenantAsync(cs, "alfaco", 111.00m, "Start");
        }
        else
        {
            alfa = await Seed.TenantAsync(cs, "alfaco", 111.00m, "Start");
            beta = await Seed.TenantAsync(cs, "betaco", 222.00m, "Biznes");
        }
        await Seed.InvoiceAsync(cs, alfa, 2026, 10, 111.00m, 7, "ALFA-NOTE");
        if (originalRowRewritten)
            await Seed.TouchTenantAsync(cs, Seed.OriginalTenantId);

        var companies = new List<Company>
        {
            new(Seed.OriginalTenantSlug, Seed.OriginalTenantId, 999.00m, "Enterprise", new[] { "ORIGINAL-SECRET-NOTE" }, "admin@bax.test"),
            new("alfaco", alfa, 111.00m, "Start", new[] { "ALFA-NOTE" }, "admin@alfaco.test"),
            new("betaco", beta, 222.00m, "Biznes", Array.Empty<string>(), "admin@betaco.test"),
        };
        foreach (var c in companies)
        {
            var location = await Seed.LocationAsync(cs, c.Id);
            await Seed.EmployeeAsync(cs, c.Id, location, c.AdminEmail, Pin, EmployeeRole.Admin);
            if (operatorId is Guid op && c.Id == Seed.OriginalTenantId)
                await Seed.EmployeeAsync(cs, c.Id, location, "operator@platform.test", Pin, EmployeeRole.Admin, op);
        }
        return companies;
    }
}
