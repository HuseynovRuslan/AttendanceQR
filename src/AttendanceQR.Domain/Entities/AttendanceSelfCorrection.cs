namespace AttendanceQR.Domain.Entities;

/// <summary>
/// A correction somebody made to their OWN attendance — kept for good.
///
/// Nobody corrects the day they are paid for (the owner, 2026-10-15) — except where the company has nobody
/// else who could: a company with one admin would otherwise have that admin's wrong days stay wrong for ever.
/// There the correction goes ahead, but in the open (docs/strukturlar-ve-selahiyyetler.md §18): a reason is
/// required, the day is recomputed with the new times while the old ones stay here, the audit line has its own
/// event, and every screen that shows the day marks it «öz düzəlişi».
///
/// Append-only. A row is written once and never changed or deleted: the history of what a person did to their
/// own pay is not editable by that person, or by anybody. Where the company asks for a second pair of eyes
/// (<see cref="Tenant.SelfCorrectionNeedsApproval"/>) the correction is written here as AWAITING and nothing is
/// applied; the approver's answer is a second row that names it (<see cref="DecisionForId"/>).
/// </summary>
public class AttendanceSelfCorrection : ITenantScoped
{
    public AttendanceSelfCorrection()
    {
        Id = Guid.NewGuid();
        AtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    /// <summary>Whose day it is — on a correction, also who made it.</summary>
    public Guid EmployeeId { get; set; }

    /// <summary>Who wrote this row: the person themselves, or — on a decision row — the approver.</summary>
    public Guid ActorEmployeeId { get; set; }

    /// <summary>The day's attendance record, once there is one (a creation awaiting approval has none yet).</summary>
    public Guid? RecordId { get; set; }

    /// <summary>The day, in COMPANY time — the date the record carries.</summary>
    public DateOnly AttendanceDate { get; set; }

    /// <summary>What was done: Update, Create, ClearCheckout, CloseOpen, MissedCheckout — or, on a decision row,
    /// Approved / Rejected.</summary>
    public string Kind { get; set; } = string.Empty;

    public DateTime? CheckInBeforeUtc { get; set; }
    public DateTime? CheckOutBeforeUtc { get; set; }
    public DateTime? CheckInAfterUtc { get; set; }
    public DateTime? CheckOutAfterUtc { get; set; }

    /// <summary>Why, in their own words (3–500 characters, as every correction).</summary>
    public string Reason { get; set; } = string.Empty;

    public DateTime AtUtc { get; set; }

    /// <summary>Written while the company asks for a second approval: nothing was applied, the times above are what
    /// the approval would set.</summary>
    public bool AwaitsApproval { get; set; }

    /// <summary>On a decision row: the awaiting row it answers.</summary>
    public Guid? DecisionForId { get; set; }
}
