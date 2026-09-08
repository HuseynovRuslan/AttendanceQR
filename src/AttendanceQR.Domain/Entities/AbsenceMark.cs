namespace AttendanceQR.Domain.Entities;

/// <summary>
/// «Qayıb yaz» — a manager states, for one person on one day, that they did not come to work.
///
/// Absence in this system has always been an INFERENCE: no scan on a scheduled day means Qayıb. That
/// works for somebody with a phone in their pocket, and it is silently wrong for everybody else. On
/// 2026-09-08 the register held two hundred and twenty-seven active employees who had never scanned
/// once — most of them just imported, but ten of them long past the fourteen-day setup window and
/// quietly accumulating twenty-eight to forty-five Qayıb days each. Payroll deducts a day's pay per
/// Qayıb. Nobody had decided any of it; the absence of evidence was being read as evidence.
///
/// So the inference is dropped for anyone who has never recorded any attendance at all — they cannot
/// be judged by scans they were never able to make — and this is what replaces it: a person saying
/// so. A branch manager who watched somebody not turn up marks the day, their name goes on it, and
/// it can be taken off again.
///
/// Deliberately its own entity rather than a <see cref="LeaveRecord"/> with a new type. A leave is an
/// entitlement and payroll does not deduct it; this is its exact opposite, and the two must never be
/// reachable from the same dropdown by accident.
/// </summary>
public class AbsenceMark : ITenantScoped
{
    public AbsenceMark()
    {
        Id = Guid.NewGuid();
    }

    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid EmployeeId { get; set; }

    /// <summary>The day they did not come, in COMPANY time — the same date an AttendanceRecord carries.</summary>
    public DateOnly Date { get; set; }

    /// <summary>Why, in the manager's words. Optional, and never parsed.</summary>
    public string? Note { get; set; }

    /// <summary>Who said so. A day that costs somebody pay needs a name against it.</summary>
    public Guid? CreatedByEmployeeId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
