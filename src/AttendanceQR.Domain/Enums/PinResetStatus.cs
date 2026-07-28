namespace AttendanceQR.Domain.Enums;

/// <summary>Lifecycle of a <see cref="Entities.PinResetRequest"/>: Pending until an admin either
/// Resolved it (reset the PIN) or Dismissed it (bogus / already handled).</summary>
public enum PinResetStatus
{
    Pending = 0,
    Resolved = 1,
    Dismissed = 2
}
