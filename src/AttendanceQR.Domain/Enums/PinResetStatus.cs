namespace AttendanceQR.Domain.Enums;

/// <summary>Lifecycle of a <see cref="Entities.PinResetRequest"/>: Pending until an admin or manager either
/// Resolved it (reset the PIN) or Dismissed it (bogus / already handled) — or the employee got back in
/// without it (Recovered).</summary>
public enum PinResetStatus
{
    Pending = 0,
    Resolved = 1,
    Dismissed = 2,

    // The employee signed in with a PIN that worked — remembered it, or was handed one another way — so
    // the request closed itself. Left open, it is an offer to reset the PIN of somebody who is already
    // back in, and acting on it signs them out. On 05.10.2026, 62 of Bakı Abadlıq's 70 open requests were
    // from people who had long since got in.
    Recovered = 3
}
