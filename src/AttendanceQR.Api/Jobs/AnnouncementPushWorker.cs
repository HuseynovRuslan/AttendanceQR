using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Jobs;

/// <summary>
/// Sends announcement push notifications out-of-band. Two triggers, one send path:
///  - the queue — an admin posted an unscheduled announcement and the request returned immediately;
///  - a periodic sweep — scheduled announcements whose time has come. (The Create endpoint used to
///    claim "the reminder job picks it up when it comes due" about a job that did not exist:
///    scheduled announcements appeared as banners but their push never went out.)
/// PushedAtUtc is the idempotency mark: set after a successful fan-out, checked before every send,
/// so the sweep can run forever without double-pushing and a retired announcement is never pushed.
/// </summary>
public sealed class AnnouncementPushWorker : BackgroundService
{
    private static readonly TimeSpan SweepEvery = TimeSpan.FromMinutes(1);

    private readonly IAnnouncementPushQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AnnouncementPushWorker> _logger;

    public AnnouncementPushWorker(
        IAnnouncementPushQueue queue, IServiceScopeFactory scopeFactory, ILogger<AnnouncementPushWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        => await Task.WhenAll(DrainQueueAsync(stoppingToken), SweepScheduledAsync(stoppingToken));

    private async Task DrainQueueAsync(CancellationToken ct)
    {
        await foreach (var job in _queue.Reader.ReadAllAsync(ct))
        {
            try
            {
                await PushOneAsync(job.TenantId, job.AnnouncementId, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AnnouncementPushWorker: push for {AnnouncementId} failed", job.AnnouncementId);
            }
        }
    }

    private async Task SweepScheduledAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(SweepEvery, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                List<Guid> tenantIds;
                using (var listScope = _scopeFactory.CreateScope())
                {
                    var db = listScope.ServiceProvider.GetRequiredService<AppDbContext>();
                    tenantIds = await db.Tenants.Where(t => t.IsActive).Select(t => t.Id).ToListAsync(ct);
                }

                var nowUtc = DateTime.UtcNow;
                foreach (var tenantId in tenantIds)
                {
                    using var scope = _scopeFactory.CreateScope();
                    scope.ServiceProvider.GetRequiredService<ITenantContext>().Resolve(tenantId);
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    var due = await db.Announcements
                        .Where(a => a.IsActive && a.PushedAtUtc == null
                                    && a.ScheduledForUtc != null && a.ScheduledForUtc <= nowUtc)
                        .Select(a => a.Id)
                        .ToListAsync(ct);

                    foreach (var id in due)
                        await PushOneAsync(tenantId, id, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AnnouncementPushWorker: scheduled sweep failed");
            }
        }
    }

    private async Task PushOneAsync(Guid tenantId, Guid announcementId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Resolve(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<IPushNotifier>();

        var announcement = await db.Announcements.FirstOrDefaultAsync(a => a.Id == announcementId, ct);
        // Re-read, not trusted from the queue: a retire or delete that landed since wins, and
        // PushedAtUtc makes a duplicate enqueue (or the sweep racing the queue) a no-op.
        if (announcement is null || !announcement.IsActive || announcement.PushedAtUtc is not null)
            return;
        if (announcement.ScheduledForUtc is DateTime scheduled && scheduled > DateTime.UtcNow)
            return; // not due yet — the sweep will come back for it

        var ids = await ResolveAudienceAsync(db, announcement, ct);
        var title = string.IsNullOrWhiteSpace(announcement.Title) ? "Yeni elan" : announcement.Title!;
        // The banner carries the full text; the notification just has to get them to open it.
        var body = announcement.Message.Length > 160 ? announcement.Message[..157] + "…" : announcement.Message;
        var pushed = await notifier.NotifyEmployeesAsync(ids, title, body, "/home", ct);

        announcement.PushedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "AnnouncementPushWorker: announcement {AnnouncementId} pushed to {Pushed}/{Audience} employees",
            announcementId, pushed, ids.Count);
    }

    /// <summary>Resolves an announcement's audience to employee ids. Mirrors the employee-side filter
    /// in AnnouncementsController so both agree on who it's for.</summary>
    private static async Task<List<Guid>> ResolveAudienceAsync(AppDbContext db, Announcement a, CancellationToken ct)
    {
        if (a.Audience == AnnouncementAudience.Selected)
        {
            return await db.AnnouncementRecipients
                .Where(r => r.AnnouncementId == a.Id).Select(r => r.EmployeeId).ToListAsync(ct);
        }

        var active = db.Employees.Where(e => e.IsActive);
        if (a.Audience is AnnouncementAudience.AtWork or AnnouncementAudience.NotAtWork)
        {
            // "At work" = checked in today. Records are keyed by the server UTC day (scan handler).
            var todayUtc = DateOnly.FromDateTime(DateTime.UtcNow);
            var inToday = db.AttendanceRecords
                .Where(r => r.AttendanceDate == todayUtc && r.CheckInAtUtc != null)
                .Select(r => r.EmployeeId);
            active = a.Audience == AnnouncementAudience.AtWork
                ? active.Where(e => inToday.Contains(e.Id))
                : active.Where(e => !inToday.Contains(e.Id));
        }
        return await active.Select(e => e.Id).ToListAsync(ct);
    }
}
