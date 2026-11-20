namespace AttendanceQR.Domain.Entities;

/// <summary>
/// One report, for one recipient, for one day — and the record that it went out.
///
/// The figures are frozen in <see cref="SnapshotJson"/> when the row is created, and the PDF, the HTML
/// page behind the link and every retry are rendered from that snapshot. So the file in Telegram and
/// the page the button opens can never disagree, however long a retry waits, and a link opened days
/// later still shows the day as it was reported.
///
/// A scheduled send is unique per (company, recipient, day) — the database refuses a second one, so a
/// restart or a slow tick can never send the leadership the same report twice. A manual «send now»
/// is not limited.
/// </summary>
public class DailyReportDelivery : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid RecipientId { get; set; }

    /// <summary>The local day the report describes.</summary>
    public DateOnly ReportDate { get; set; }

    /// <summary>0 = scheduled (the daily send), 1 = sent by hand from the panel.</summary>
    public int Kind { get; set; }

    /// <summary>0 = waiting to be sent, 1 = sent, 2 = given up (attempts exhausted).</summary>
    public int Status { get; set; }
    public int Attempts { get; set; }
    public DateTime NextAttemptUtc { get; set; } = DateTime.UtcNow;

    /// <summary>The last failure, already cleaned of anything secret. Never a token, never a chat id.</summary>
    public string? LastError { get; set; }

    /// <summary>The report's figures at the moment it was made (DailyReportData as JSON). No personal data — counts only.</summary>
    public string SnapshotJson { get; set; } = "{}";

    /// <summary>SHA-256 of the secret part of the «open the report» link. Only the hash is stored.</summary>
    public string TokenHash { get; set; } = string.Empty;
    public DateTime TokenExpiresUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SentAtUtc { get; set; }
}
