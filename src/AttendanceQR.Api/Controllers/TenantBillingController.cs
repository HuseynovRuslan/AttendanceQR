using AttendanceQR.Domain;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// What the CUSTOMER sees about their own subscription.
///
/// Billing already existed, but only in the operator console — the company paying for this had no way
/// to see what package they were on, what this month costs, or whether last month was settled. They
/// asked by telephone, and the answer came from a screen only we could open.
///
/// Everything here is read-only and self-scoped. A tenant admin cannot change their plan, mark a bill
/// paid, or see another company: pricing and collection stay with the operator, which is what they
/// stay with in every product that sells this way. What changes is that the customer can now check.
/// </summary>
[ApiController]
[Route("api/tenant/billing")]
[Authorize(Roles = "Admin")]
public class TenantBillingController : ControllerBase
{
    private readonly AppDbContext _db;

    public TenantBillingController(AppDbContext db) => _db = db;

    /// <summary>How many past periods to hand back. Two years is more history than anyone scrolls and
    /// still small enough to send in one response.</summary>
    private const int HistoryMonths = 24;

    // GET /api/tenant/billing — plan, demo status, this month's amount and the payment history.
    [HttpGet]
    public async Task<IActionResult> Mine()
    {
        var ct = HttpContext.RequestAborted;

        // The tenant is resolved from the token before any query runs (fail-closed multi-tenancy), so
        // the filtered DbSet already answers for exactly one company.
        var tenant = await _db.Tenants.FirstOrDefaultAsync(ct);
        if (tenant is null)
            return NotFound(new { error = "TenantNotFound" });

        // The same two counts the operator's own billing screen prices on, so the customer and the
        // invoice can never disagree about how many people were counted.
        var employees = await _db.Employees.CountAsync(e => e.IsActive, ct);
        var locations = await _db.Locations.CountAsync(l => l.IsActive, ct);

        var formula = Pricing.MonthlyAmount(employees, locations);
        var amount = tenant.MonthlyPriceOverride ?? formula;

        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        var trialEnds = tenant.TrialEndsAtUtc is DateTime t ? DateOnly.FromDateTime(t) : (DateOnly?)null;
        var onTrial = trialEnds is DateOnly te && te >= today;

        var since = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-HistoryMonths);
        var invoices = await _db.TenantInvoices
            .Where(i => i.TenantId == tenant.Id)
            .OrderByDescending(i => i.PeriodYear).ThenByDescending(i => i.PeriodMonth)
            .Select(i => new
            {
                year = i.PeriodYear,
                month = i.PeriodMonth,
                amount = i.Amount,
                employeeCount = i.EmployeeCount,
                isPaid = i.IsPaid,
                paidAtUtc = i.PaidAtUtc,
                note = i.Note,
            })
            .ToListAsync(ct);
        invoices = invoices
            .Where(i => new DateTime(i.year, i.month, 1, 0, 0, 0, DateTimeKind.Utc) >= since)
            .ToList();

        return Ok(new
        {
            plan = tenant.Plan,
            // Which published package this head-count falls in, whether or not a plan was ever set on
            // the tenant. Without it a company with no Plan row saw a blank where the price came from.
            packageByHeadcount = PackageName(employees),

            onTrial,
            trialEndsAtUtc = tenant.TrialEndsAtUtc,
            trialDaysLeft = onTrial ? trialEnds!.Value.DayNumber - today.DayNumber : 0,
            // A demo that has already ended still needs saying — the screen reads differently from one
            // that never had a demo at all.
            trialEnded = trialEnds is DateOnly ended && ended < today,

            employees,
            locations,
            maxEmployees = tenant.MaxEmployees,
            maxLocations = tenant.MaxLocations,

            // The breakdown, not just the total: "why is it 248 ₼" is the question a bill gets asked,
            // and answering it on the screen is cheaper than answering it on the telephone.
            monthly = new
            {
                amount,
                isNegotiated = tenant.MonthlyPriceOverride != null,
                formulaAmount = formula,
                ratePerEmployee = employees > 0 ? Pricing.RatePerEmployee(employees) : 0m,
                employeeTotal = employees > 0 ? employees * Pricing.RatePerEmployee(employees) : 0m,
                locationFee = Pricing.LocationMonthlyFee,
                locationTotal = locations * Pricing.LocationMonthlyFee,
            },

            invoices,
        });
    }

    /// <summary>The published package a head-count falls into. Mirrors <see cref="Pricing"/>; if the
    /// bands move, they move in one place and this follows.</summary>
    private static string PackageName(int employees) => employees switch
    {
        <= 0 => "—",
        <= 10 => "Start",
        <= 50 => "Biznes",
        <= 100 => "Korporativ",
        _ => "Enterprise",
    };
}
