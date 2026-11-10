using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Domain.Entities;

/// <summary>
/// «Mövcudluq yoxlaması» — one moment, during a day somebody is checked in, when they are asked where they are.
///
/// The app is a web app. On an iPhone it cannot read a position in the background, so nobody can be followed
/// through a day: a person can check in at the branch, spend the afternoon somewhere else and come back to check
/// out, and the record says they were there all along. What a web app can do is ask. A manager who has a reason
/// to doubt one particular person switches this on for them, for a limited time
/// (<see cref="Employee.PresenceCheckUntilUtc"/>); on each of their open days up to two of these are planned at
/// random moments, the phone is pushed, and the person answers with one tap that sends the phone's position.
///
/// Nothing here ever blocks or changes the day. An answer from outside the radius is told to the manager; no answer
/// at all is only counted, because a busy person and an absent one cannot be told apart from here.
///
/// The plan lives in this table, not in a worker's memory: a deploy restart must not lose a planned check, and
/// <see cref="SentAtUtc"/> being set before the push goes out is what keeps a check from being sent twice.
/// </summary>
public class PresenceCheck : ITenantScoped
{
    public PresenceCheck()
    {
        Id = Guid.NewGuid();
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid EmployeeId { get; set; }

    /// <summary>The open day the check belongs to. Closing that day before the check is sent cancels it.</summary>
    public Guid AttendanceRecordId { get; set; }

    /// <summary>The branch of that day — what the answer is measured against. Not a foreign key: a branch can be
    /// deleted without this table standing in the way.</summary>
    public Guid LocationId { get; set; }

    /// <summary>The company day of <see cref="AttendanceRecordId"/>; the at-most-two-a-day rule counts by it.</summary>
    public DateOnly CheckDate { get; set; }

    /// <summary>0 or 1 — which of the day's checks this is. With the record, the uniqueness that keeps a sweep
    /// from planning the same day twice.</summary>
    public int Slot { get; set; }

    /// <summary>When it is to be asked. Chosen at random inside the day's window, never shown to anyone.</summary>
    public DateTime DueAtUtc { get; set; }

    /// <summary>When the push went out. Set (and saved) BEFORE the push, so a second sweep cannot send it again.</summary>
    public DateTime? SentAtUtc { get; set; }

    /// <summary>How many of the person's devices the push reached. 0 is common on an iPhone without the app on the
    /// home screen — the home-page card is what asks them then.</summary>
    public int? Reached { get; set; }

    public PresenceCheckResult Result { get; set; } = PresenceCheckResult.Pending;

    /// <summary>Why a check was cancelled or not judged: «DayClosed», «FieldVisit», «Disabled», «Stale»,
    /// «Inactive», or the phone's GPS failure for <see cref="PresenceCheckResult.GpsFailed"/>.</summary>
    public string? Note { get; set; }

    public DateTime? RespondedAtUtc { get; set; }

    /// <summary>Answered after the window had run out. Accepted all the same — the position is what matters.</summary>
    public bool Late { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public double? AccuracyMeters { get; set; }

    public double? DistanceMeters { get; set; }

    /// <summary>The branch's radius at the moment of the answer, so the history still reads right after the
    /// radius is changed.</summary>
    public int? RadiusMeters { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
