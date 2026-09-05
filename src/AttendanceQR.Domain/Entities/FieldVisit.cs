namespace AttendanceQR.Domain.Entities;

/// <summary>Where a field visit is in its lifecycle. Stored as int; values are stable, never renumber.</summary>
public enum FieldVisitStatus
{
    /// <summary>A manager created it; the worker has not arrived yet.</summary>
    Assigned = 0,

    /// <summary>The worker arrived (GPS + selfie) and is on site.</summary>
    CheckedIn = 1,

    /// <summary>The worker checked out — the visit is finished.</summary>
    Completed = 2,

    /// <summary>The assignment was called off before the worker arrived.</summary>
    Cancelled = 3,
}

/// <summary>
/// A field / mobile attendance visit — a worker sent to an ad-hoc site where no printed QR poster can be
/// put up (the whole reason this exists). Proof of presence is GPS + selfie + timestamp, reusing the
/// same machinery a scan uses, NOT a scan. A manager can ASSIGN one (optionally pinning a target the
/// arrival GPS is measured against) or a worker can self-report one. Arrival and departure are both
/// captured, so the time on site is known. Like a scan, nothing here BLOCKS the worker — a missing
/// photo or a far-from-target GPS flags the visit for the manager; it never refuses to record it.
/// </summary>
public class FieldVisit : ITenantScoped
{
    /// <summary>
    /// The manager's verdict AFTER the work was done: was the job actually resolved?
    ///
    /// Status answers a different question — Completed means the worker checked out, which says they
    /// left, not that the yard is clean. A rota of small jobs («süpür», «zibili boşalt», «kanalı
    /// təmizlə») is only worth assigning if somebody looks afterwards and says yes or no, and until
    /// this there was nowhere to put that answer: the board's only two endings were «Tamamlandı»,
    /// written by the worker, and «Ləğv», which erases the visit rather than judging it.
    ///
    /// Null while nobody has looked yet. Never blocks anything and never changes the hours — this is
    /// a note on the work, not on the attendance.
    /// </summary>
    public bool? ReviewOk { get; set; }

    /// <summary>When the verdict was recorded; null while unreviewed.</summary>
    public DateTime? ReviewedAtUtc { get; set; }

    /// <summary>Who looked. Kept so «həll olunmadı» has a name against it, like every other judgement
    /// in this product — an anonymous rejection is one nobody can ask about.</summary>
    public Guid? ReviewedByEmployeeId { get; set; }

    /// <summary>Why, in the reviewer's own words. Optional, and the only free text on the verdict.</summary>
    public string? ReviewNote { get; set; }

    public Guid Id { get; set; } = Guid.NewGuid();

    // Multi-tenancy: which company this row belongs to (auto-stamped on save).
    public Guid TenantId { get; set; }

    /// <summary>The worker doing the visit.</summary>
    public Guid EmployeeId { get; set; }

    /// <summary>Who assigned it. Null when the worker self-reported the visit.</summary>
    public Guid? AssignedByEmployeeId { get; set; }

    /// <summary>The local (Baku) day this visit belongs to — the board groups by it.</summary>
    public DateOnly VisitDate { get; set; }

    public FieldVisitStatus Status { get; set; } = FieldVisitStatus.Assigned;

    // ---- Target (optional). Set when a manager pins a place; all null for a fully ad-hoc visit. ----
    public string? TargetLabel { get; set; }
    public double? TargetLatitude { get; set; }
    public double? TargetLongitude { get; set; }
    public int? TargetRadiusMeters { get; set; }

    public DateTime? AssignedAtUtc { get; set; }

    // ---- Arrival ----
    public DateTime? CheckInAtUtc { get; set; }
    public double? CheckInLatitude { get; set; }
    public double? CheckInLongitude { get; set; }
    public string? CheckInPhotoKey { get; set; }

    /// <summary>Metres between the arrival GPS and the target (null when there is no target). Advisory
    /// only — the board shows "in the area" vs "N away"; it never blocks the check-in.</summary>
    public double? CheckInDistanceMeters { get; set; }

    // ---- Departure ----
    public DateTime? CheckOutAtUtc { get; set; }
    public double? CheckOutLatitude { get; set; }
    public double? CheckOutLongitude { get; set; }
    public string? CheckOutPhotoKey { get; set; }

    /// <summary>
    /// İŞ ŞƏKLİ — object key of a photo of the WORK (a swept yard, an emptied bin), taken with the
    /// REAR camera at check-out. Deliberately NOT <see cref="CheckOutPhotoKey"/>: that slot is a FACE,
    /// this is a PLACE. Different prefix (<c>fieldwork/</c> vs <c>checkins/</c>), different endpoint,
    /// different audience, and never fed to the face-match worker — a NoFace flag on a bin would be
    /// nonsense that pollutes the real queue. Null when the worker sent none: a missing work photo
    /// never blocks a check-out, it just leaves the visit without evidence.
    /// </summary>
    public string? WorkPhotoKey { get; set; }

    public DateTime? WorkPhotoAtUtc { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
