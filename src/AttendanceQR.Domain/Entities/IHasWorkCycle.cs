namespace AttendanceQR.Domain.Entities;

/// <summary>
/// Something that can carry a rotation ("növbə"): a cycle length, how many of its first days are
/// worked, and one date it is known to be ON.
///
/// Implemented by both <see cref="Schedule"/> and <see cref="Employee"/> — a shift normally owns the
/// rotation, but an employee who is not on a named shift can still have one of their own. The two
/// must be validated identically or one route into the data would accept a cycle the other rejects,
/// so <c>WorkCycle.Apply</c> writes through this rather than through either type.
/// </summary>
public interface IHasWorkCycle
{
    int? WorkCycleDays { get; set; }
    int WorkCycleOnDays { get; set; }
    DateOnly? WorkCycleAnchor { get; set; }
}
