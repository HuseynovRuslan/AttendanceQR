namespace AttendanceQR.Infrastructure.Security;

public class InvitationOptions
{
    public const string SectionName = "Invitation";

    /// <summary>How long an activation token stays valid. Default 72 hours.</summary>
    public int ExpiryHours { get; set; } = 72;
}
