namespace AttendanceQR.Domain.Entities;

/// <summary>
/// A person in the company's leadership who receives the daily attendance report on Telegram.
///
/// Not an Employee: a director or a group head may have no account here at all, and must not need one
/// to read a report. Who they are to Telegram is learned, never typed: the admin hands them a one-time
/// link (<see cref="LinkCodeHash"/>), they press «Start» in the bot, and the bot stores the chat it was
/// pressed from. Typing a chat id would let a mistyped digit send the company's figures to a stranger.
/// </summary>
public class DailyReportRecipient : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>How the admin refers to this person («Direktor», «Vüqar müəllim»). Shown in the panel only.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The branches whose figures this person receives, comma-separated ids. Null or empty = the whole
    /// company. Re-read at every send, so a branch that was deleted simply drops out.
    /// </summary>
    public string? LocationIds { get; set; }

    /// <summary>
    /// When this person gets the report every day, in the company's local time. Null = never on its own —
    /// only when somebody presses «Hesabatı indi göndər». Per recipient (not per company) so whoever adds a
    /// recipient — a structure manager for their own branches as much as the company admin — decides when.
    /// </summary>
    public TimeOnly? SendAtLocal { get; set; }

    /// <summary>The Telegram chat bound by pressing «Start» on the link. Null = not connected yet.</summary>
    public long? TelegramChatId { get; set; }
    public DateTime? LinkedAtUtc { get; set; }

    /// <summary>SHA-256 of the secret part of the current «connect» link; null once used. Only the hash is kept.</summary>
    public string? LinkCodeHash { get; set; }
    public DateTime? LinkCodeExpiresUtc { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? CreatedByEmployeeId { get; set; }
}
