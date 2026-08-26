using System.Security.Claims;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Domain;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// What a paying company can see about its own subscription.
///
/// Two things must hold, and they are the reasons this screen exists at all. The amount the customer
/// reads has to be the amount the operator bills — otherwise the screen is worse than nothing, because
/// it manufactures an argument at invoice time. And a company must see only its own money.
///
/// The demo is informational by design: when it ends nothing switches off. A trial that locked people
/// out of clocking in would cost them a day's pay over a billing question, which this product never
/// trades away — the same reason a missing photo does not block a scan.
/// </summary>
public class TenantBillingTests
{
    private static readonly Guid TenantA = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid TenantB = Guid.Parse("00000000-0000-0000-0000-0000000000b1");

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public TenantBillingController Controller { get; }
        public Guid AdminId { get; } = Guid.NewGuid();

        public Harness(int employees = 12, int locations = 2, DateTime? trialEndsAtUtc = null,
                       decimal? priceOverride = null)
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantA);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase($"tenant-billing-{Guid.NewGuid()}").Options,
                tenant);

            Db.Tenants.Add(new Tenant
            {
                Id = TenantA, Name = "A", Slug = "a", DisplayName = "Şirkət A", IsActive = true,
                TrialEndsAtUtc = trialEndsAtUtc, MonthlyPriceOverride = priceOverride,
            });

            for (var i = 0; i < employees; i++)
                Db.Employees.Add(new Employee
                {
                    Id = Guid.NewGuid(), TenantId = TenantA, FullName = $"İşçi {i}",
                    Role = EmployeeRole.Employee, IsActive = true, PasswordHash = "h",
                    LocationId = Guid.NewGuid(),
                });

            for (var i = 0; i < locations; i++)
                Db.Locations.Add(new Location
                {
                    Id = Guid.NewGuid(), TenantId = TenantA, Name = $"Filial {i}", IsActive = true,
                });

            Db.SaveChanges();

            Controller = new TenantBillingController(Db)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                        {
                            new Claim("sub", AdminId.ToString()),
                            new Claim("role", "Admin"),
                            new Claim("tid", TenantA.ToString()),
                        }, "test")),
                    },
                },
            };
        }

        /// <summary>Reads the anonymous response object's property by name.</summary>
        public static object? Get(object payload, string name)
            => payload.GetType().GetProperty(name)!.GetValue(payload);

        public async Task<object> LoadAsync()
        {
            var result = Assert.IsType<OkObjectResult>(await Controller.Mine());
            return result.Value!;
        }

        public void Dispose() => Db.Dispose();
    }

    [Fact]
    public async Task The_amount_shown_is_the_amount_the_operator_bills()
    {
        // The whole point. If these two ever diverge the screen becomes a source of disputes rather
        // than an answer to them, so it is pinned against the same formula the invoice uses.
        using var h = new Harness(employees: 12, locations: 2);

        var body = await h.LoadAsync();
        var monthly = Harness.Get(body, "monthly")!;

        Assert.Equal(Pricing.MonthlyAmount(12, 2), Harness.Get(monthly, "amount"));
        // 12 people is Biznes: 12 × 3.5 + 2 × 5 = 52.
        Assert.Equal(52m, Harness.Get(monthly, "amount"));
        Assert.Equal(3.5m, Harness.Get(monthly, "ratePerEmployee"));
    }

    [Fact]
    public async Task A_negotiated_price_replaces_the_formula_and_says_so()
    {
        using var h = new Harness(employees: 200, locations: 9, priceOverride: 450m);

        var monthly = Harness.Get(await h.LoadAsync(), "monthly")!;

        Assert.Equal(450m, Harness.Get(monthly, "amount"));
        Assert.Equal(true, Harness.Get(monthly, "isNegotiated"));
        // The formula figure is still handed back — the customer sees what the list price would be.
        Assert.Equal(Pricing.MonthlyAmount(200, 9), Harness.Get(monthly, "formulaAmount"));
    }

    [Fact]
    public async Task A_demo_in_the_future_reads_as_a_demo_with_days_left()
    {
        using var h = new Harness(trialEndsAtUtc: DateTime.UtcNow.Date.AddDays(10));

        var body = await h.LoadAsync();

        Assert.Equal(true, Harness.Get(body, "onTrial"));
        Assert.Equal(false, Harness.Get(body, "trialEnded"));
        Assert.Equal(10, Harness.Get(body, "trialDaysLeft"));
    }

    [Fact]
    public async Task The_last_day_of_the_demo_is_still_inside_it()
    {
        // Off-by-one on the boundary would tell a customer their demo ended on the morning of the day
        // they were promised, which is the version of this bug that generates a telephone call.
        using var h = new Harness(trialEndsAtUtc: DateTime.UtcNow.Date);

        var body = await h.LoadAsync();

        Assert.Equal(true, Harness.Get(body, "onTrial"));
        Assert.Equal(0, Harness.Get(body, "trialDaysLeft"));
    }

    [Fact]
    public async Task A_demo_that_has_passed_reads_differently_from_never_having_had_one()
    {
        using var ended = new Harness(trialEndsAtUtc: DateTime.UtcNow.Date.AddDays(-1));
        using var never = new Harness(trialEndsAtUtc: null);

        var endedBody = await ended.LoadAsync();
        Assert.Equal(false, Harness.Get(endedBody, "onTrial"));
        Assert.Equal(true, Harness.Get(endedBody, "trialEnded"));

        var neverBody = await never.LoadAsync();
        Assert.Equal(false, Harness.Get(neverBody, "onTrial"));
        Assert.Equal(false, Harness.Get(neverBody, "trialEnded"));
    }

    [Fact]
    public async Task Only_this_company_s_invoices_come_back()
    {
        // TenantInvoice is GLOBAL — it carries no query filter, because the operator console is meant
        // to read every tenant's billing. That makes this the one place a missing WHERE would hand one
        // customer another customer's money, so it is filtered explicitly and pinned here.
        using var h = new Harness();
        h.Db.TenantInvoices.Add(new TenantInvoice
        {
            TenantId = TenantA, PeriodYear = DateTime.UtcNow.Year, PeriodMonth = DateTime.UtcNow.Month,
            Amount = 52m, EmployeeCount = 12, IsPaid = true, PaidAtUtc = DateTime.UtcNow,
        });
        h.Db.TenantInvoices.Add(new TenantInvoice
        {
            TenantId = TenantB, PeriodYear = DateTime.UtcNow.Year, PeriodMonth = DateTime.UtcNow.Month,
            Amount = 999m, EmployeeCount = 300, IsPaid = false,
        });
        await h.Db.SaveChangesAsync();

        var invoices = (System.Collections.IEnumerable)Harness.Get(await h.LoadAsync(), "invoices")!;
        var rows = invoices.Cast<object>().ToList();

        Assert.Single(rows);
        Assert.Equal(52m, Harness.Get(rows[0], "amount"));
    }

    [Fact]
    public async Task An_empty_company_bills_nothing_even_with_branches()
    {
        // The location fee rides on a live subscription; it is not a standalone charge on a company
        // that is not using the system. Pinned so the customer-facing screen cannot start showing a
        // bill to somebody who has not started yet.
        using var h = new Harness(employees: 0, locations: 3);

        var monthly = Harness.Get(await h.LoadAsync(), "monthly")!;

        Assert.Equal(0m, Harness.Get(monthly, "amount"));
    }
}
