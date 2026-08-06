namespace AttendanceQR.Domain.Entities;

/// <summary>
/// One company's bill for one month. Billing lives in the platform operator console (across tenants), so
/// like <see cref="SuperAdminAuditLog"/> this is GLOBAL — it carries no TenantId query filter; the
/// operator is meant to see every tenant's billing.
///
/// A row exists once a period has been TOUCHED (marked paid/unpaid, amount recorded). A period with no
/// row is treated as unpaid and its amount computed live from the current employee count — so nothing
/// has to be pre-generated each month. The stored <see cref="Amount"/>/<see cref="EmployeeCount"/> are a
/// snapshot taken when the row is written, so a later head-count change never rewrites a settled bill.
/// One row per (tenant, year, month) — enforced by a unique index.
/// </summary>
public class TenantInvoice
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    public int PeriodYear { get; set; }

    /// <summary>1–12.</summary>
    public int PeriodMonth { get; set; }

    /// <summary>AZN billed for the period — snapshot when the row was written.</summary>
    public decimal Amount { get; set; }

    /// <summary>Active-employee count the amount was based on — snapshot.</summary>
    public int EmployeeCount { get; set; }

    public bool IsPaid { get; set; }

    public DateTime? PaidAtUtc { get; set; }

    /// <summary>The operator who last set the status.</summary>
    public Guid? MarkedByEmployeeId { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
