using AttendanceQR.Api.Contracts;
using AttendanceQR.Domain;
using AttendanceQR.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

// Billing lives in the operator console, across tenants. Each company's monthly bill = its negotiated
// flat price if set, else the published package formula (Pricing): flat per-employee rate by
// head-count package + 5 AZN per active location. Payment is tracked per (tenant, month) in
// TenantInvoice: a period with no row is unpaid and priced live; marking it writes a snapshot so a
// later head-count change never rewrites a settled bill.
public partial class SuperAdminController
{
    // GET /api/super/billing?year=&month= — every active company's bill for a month + the totals.
    [HttpGet("billing")]
    public async Task<IActionResult> Billing([FromQuery] int? year, [FromQuery] int? month)
    {
        if (!await CanAsync(OperatorPermission.ViewBusiness, HttpContext.RequestAborted))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "NotSuperAdmin" });

        var ct = HttpContext.RequestAborted;
        var now = DateTime.UtcNow;
        var y = year is >= 2000 and <= 9999 ? year.Value : now.Year;
        var m = month is >= 1 and <= 12 ? month.Value : now.Month;

        var tenants = await _db.Tenants.Where(t => t.IsActive).ToListAsync(ct);

        var empCounts = await _db.Employees.IgnoreQueryFilters()
            .Where(e => e.IsActive)
            .GroupBy(e => e.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, ct);

        // The published model adds 5 AZN per active location, so the live price needs the count.
        var locCounts = await _db.Locations.IgnoreQueryFilters()
            .Where(l => l.IsActive)
            .GroupBy(l => l.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, ct);

        var invoices = await _db.TenantInvoices
            .Where(i => i.PeriodYear == y && i.PeriodMonth == m)
            .ToDictionaryAsync(i => i.TenantId, ct);

        // A tenant disabled AFTER settling a bill still counts toward that month's totals — its snapshot
        // is the record of paid revenue. Pull in any tenant that has an invoice for the period but has
        // since dropped off the active list, so churn never erases historical collected/billed.
        var activeIds = tenants.Select(t => t.Id).ToHashSet();
        var churnedWithInvoice = invoices.Keys.Where(k => !activeIds.Contains(k)).ToList();
        if (churnedWithInvoice.Count > 0)
            tenants.AddRange(await _db.Tenants.Where(t => churnedWithInvoice.Contains(t.Id)).ToListAsync(ct));

        var rows = tenants
            .OrderBy(t => t.DisplayName)
            .Select(t =>
            {
                var liveEmp = empCounts.GetValueOrDefault(t.Id, 0);
                invoices.TryGetValue(t.Id, out var inv);
                // A settled period reads back its snapshot; an untouched period is priced live.
                var employeeCount = inv?.EmployeeCount ?? liveEmp;
                var amount = inv?.Amount ?? t.MonthlyPriceOverride
                    ?? Pricing.MonthlyAmount(liveEmp, locCounts.GetValueOrDefault(t.Id, 0));
                return new
                {
                    tenantId = t.Id,
                    slug = t.Slug,
                    displayName = t.DisplayName,
                    plan = t.Plan,
                    isActive = t.IsActive,
                    employeeCount,
                    amount,
                    priceOverride = t.MonthlyPriceOverride,
                    isPaid = inv?.IsPaid ?? false,
                    paidAtUtc = inv?.PaidAtUtc,
                    note = inv?.Note,
                    settled = inv != null,
                };
            }).ToList();

        var billed = rows.Sum(r => r.amount);
        var collected = rows.Where(r => r.isPaid).Sum(r => r.amount);

        return Ok(new
        {
            year = y,
            month = m,
            totals = new
            {
                billed,
                collected,
                outstanding = billed - collected,
                paidCount = rows.Count(r => r.isPaid),
                totalCount = rows.Count,
            },
            rows,
        });
    }

    // POST /api/super/tenants/{id}/billing — mark a company's bill for a period paid/unpaid. Upserts the
    // invoice (unique per tenant+period) and snapshots the amount + head-count. Amount defaults to the
    // negotiated/ formula price but can be overridden for this one bill.
    [HttpPost("tenants/{id:guid}/billing")]
    public async Task<IActionResult> MarkBilling(Guid id, [FromBody] BillingMarkRequest request)
    {
        if (!await CanAsync(OperatorPermission.Billing, HttpContext.RequestAborted))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden" });

        var ct = HttpContext.RequestAborted;
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null)
            return NotFound(new { error = "TenantNotFound" });

        // Writing money — require an explicit, in-range period rather than silently defaulting to "now",
        // so a mistaken/omitted period can never settle the wrong month's bill.
        if (request.Year is not (>= 2000 and <= 9999) || request.Month is not (>= 1 and <= 12))
            return BadRequest(new { error = "PeriodInvalid" });
        var y = request.Year.Value;
        var m = request.Month.Value;

        var now = DateTime.UtcNow;
        var emp = await _db.Employees.IgnoreQueryFilters().CountAsync(e => e.TenantId == id && e.IsActive, ct);
        var loc = await _db.Locations.IgnoreQueryFilters().CountAsync(l => l.TenantId == id && l.IsActive, ct);
        var amount = request.Amount is decimal a and >= 0m ? a : tenant.MonthlyPriceOverride ?? Pricing.MonthlyAmount(emp, loc);
        var actor = User.EmployeeId();
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();

        void Apply(TenantInvoice iv)
        {
            iv.EmployeeCount = emp;
            iv.Amount = amount;
            iv.IsPaid = request.IsPaid;
            iv.PaidAtUtc = request.IsPaid ? now : null;
            iv.MarkedByEmployeeId = actor;
            iv.Note = note;
            iv.UpdatedAtUtc = now;
        }

        var inv = await _db.TenantInvoices
            .FirstOrDefaultAsync(i => i.TenantId == id && i.PeriodYear == y && i.PeriodMonth == m, ct);
        if (inv is null)
        {
            inv = new TenantInvoice { TenantId = id, PeriodYear = y, PeriodMonth = m, CreatedAtUtc = now };
            Apply(inv);
            _db.TenantInvoices.Add(inv);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Lost the race for this (tenant, period): the unique index rejected the insert because a
                // concurrent mark already created the row. Re-read it and re-apply idempotently — same
                // convention the scan/vote paths use for their unique-index races. Both markers compute the
                // identical amount, so whoever wins, the settled value is correct.
                _db.ChangeTracker.Clear();
                inv = await _db.TenantInvoices
                    .FirstAsync(i => i.TenantId == id && i.PeriodYear == y && i.PeriodMonth == m, ct);
                Apply(inv);
                await _db.SaveChangesAsync(ct);
            }
        }
        else
        {
            Apply(inv);
            await _db.SaveChangesAsync(ct);
        }

        await AuditAsync(request.IsPaid ? "InvoicePaid" : "InvoiceUnpaid", tenant.Id, tenant.Slug,
            $"{y}-{m:D2}: {amount:0.##} AZN", ct);

        return Ok(new { tenantId = id, year = y, month = m, amount = inv.Amount, isPaid = inv.IsPaid, paidAtUtc = inv.PaidAtUtc });
    }
}
