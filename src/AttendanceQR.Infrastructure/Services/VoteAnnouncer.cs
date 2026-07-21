using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Infrastructure.Services;

public interface IVoteAnnouncer
{
    /// <summary>
    /// Tells everyone the ballot is open, at most once per campaign. Returns true if this call is the
    /// one that sent it.
    /// </summary>
    Task<bool> AnnounceOpeningAsync(VoteCampaign campaign, DateTime localNow, CancellationToken ct);
}

/// <summary>
/// The "voting is open" notice.
///
/// Shared between the admin creating a campaign and the background sweep, because each covers a case
/// the other cannot: a ballot created already-open must be announced on the spot — the admin is
/// standing there watching for it, and waiting on the next sweep looks broken — while one scheduled
/// for a future date has nobody present when its moment arrives.
///
/// OpenedNotifiedAtUtc is what keeps the two from announcing twice.
/// </summary>
public sealed class VoteAnnouncer : IVoteAnnouncer
{
    private readonly AppDbContext _db;
    private readonly IPushNotifier _notifier;

    public VoteAnnouncer(AppDbContext db, IPushNotifier notifier)
    {
        _db = db;
        _notifier = notifier;
    }

    public async Task<bool> AnnounceOpeningAsync(VoteCampaign campaign, DateTime localNow, CancellationToken ct)
    {
        if (campaign.OpenedNotifiedAtUtc is not null || !campaign.IsOpenAt(localNow))
            return false;

        var monthName = AzMonth(campaign.Period.Month);
        var closesAt = campaign.ClosesAtLocal.ToString("dd.MM.yyyy HH:mm");

        campaign.OpenedNotifiedAtUtc = DateTime.UtcNow;
        _db.Announcements.Add(new Announcement
        {
            Title = $"{monthName} ayının işçisi — səsvermə açıldı 🗳️",
            Message = $"Öz filialınızdan bir nəfəri seçin. Səsiniz tam gizlidir — kimə səs verdiyinizi " +
                      $"heç kim görmür.\n\nSon tarix: {closesAt}. Bir dəfə səs verilir.",
            Audience = AnnouncementAudience.All,
        });
        await _db.SaveChangesAsync(ct);

        var everyone = await _db.Employees.Where(e => e.IsActive).Select(e => e.Id).ToListAsync(ct);
        await _notifier.NotifyEmployeesAsync(
            everyone, $"{monthName} ayının işçisi 🗳️",
            $"Səsvermə açıqdır — {closesAt} tarixinə qədər səs verin.", "/vote", ct);

        return true;
    }

    private static string AzMonth(int m) => m switch
    {
        1 => "Yanvar", 2 => "Fevral", 3 => "Mart", 4 => "Aprel", 5 => "May", 6 => "İyun",
        7 => "İyul", 8 => "Avqust", 9 => "Sentyabr", 10 => "Oktyabr", 11 => "Noyabr", _ => "Dekabr",
    };
}
