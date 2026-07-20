namespace AttendanceQR.Domain.Enums;

/// <summary>Who an announcement is shown to. Evaluated when the employee fetches (so "AtWork" reflects
/// their status at read time), except Selected which is a fixed recipient list.</summary>
public enum AnnouncementAudience
{
    All = 0,
    AtWork = 1,       // checked in today
    NotAtWork = 2,    // has not checked in today
    Selected = 3      // an explicit list of employees (AnnouncementRecipient rows)
}
