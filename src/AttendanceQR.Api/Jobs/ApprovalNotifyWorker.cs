using AttendanceQR.Api.Approvals;
using AttendanceQR.Application.Common;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Jobs;

/// <summary>
/// Tells the approvers about requests (the owner's rule 5 of 2026-11-11): at once about a new one, and once a day
/// about those waiting more than 24 hours.
///
/// It works from two columns of the request (NotifiedAtUtc, RemindedOn), not from the endpoint that made it. A
/// request is saved whether or not anybody can be told, a push that fails costs a banner and never a request, and
/// a restart loses nothing. The mark is written before the push, so a request is announced at most once, however
/// the push ends.
///
/// Idle while the deployment's switch is off (ApprovalOptions).
/// </summary>
public sealed class ApprovalNotifyWorker : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly ApprovalOptions _options;
    private readonly PushOptions _push;
    private readonly TimeProvider _clock;
    private readonly TimeZoneInfo _timeZone;
    private readonly ILogger<ApprovalNotifyWorker> _logger;

    public ApprovalNotifyWorker(IServiceScopeFactory scopes, ApprovalOptions options, PushOptions push, TimeProvider clock,
        AppOptions appOptions, ILogger<ApprovalNotifyWorker> logger)
    {
        _scopes = scopes;
        _options = options;
        _push = push;
        _clock = clock;
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(appOptions.TimeZone);
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (_options.Enabled)
                await SweepAllAsync(ct);
            try
            {
                await Task.Delay(Tick, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task SweepAllAsync(CancellationToken ct)
    {
        try
        {
            List<Guid> tenants;
            using (var scope = _scopes.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                tenants = await db.Tenants.Where(t => t.IsActive).Select(t => t.Id).ToListAsync(ct);
            }

            foreach (var tenantId in tenants)
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    scope.ServiceProvider.GetRequiredService<ITenantContext>().Resolve(tenantId);
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var notifier = scope.ServiceProvider.GetRequiredService<IPushNotifier>();
                    var outcome = await ApprovalNotifications.RunAsync(
                        db, notifier, _push.IsConfigured, _clock.GetUtcNow().UtcDateTime, _timeZone, _logger, ct);
                    if (outcome.Any)
                        _logger.LogInformation("Approval notifications for {Tenant}: {Outcome}", tenantId, outcome);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // One company failing must not stop the others.
                    _logger.LogError(ex, "Approval notifications for {Tenant} failed", tenantId);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Approval notification sweep failed");
        }
    }
}

public sealed record ApprovalNotifyOutcome(int Announced, int Reminded)
{
    public bool Any => Announced + Reminded > 0;
}

/// <summary>One pass over one company (the caller has resolved the tenant). An ordinary day costs one indexed
/// query: no pending request means nothing else is read.</summary>
public static class ApprovalNotifications
{
    /// <summary>The daily reminder goes out from this Baku hour on, never in the night.</summary>
    public const int ReminderHour = 10;

    public static readonly TimeSpan LongWait = TimeSpan.FromHours(24);

    public static async Task<ApprovalNotifyOutcome> RunAsync(
        AppDbContext db, IPushNotifier notifier, bool pushOn, DateTime nowUtc, TimeZoneInfo timeZone, ILogger logger,
        CancellationToken ct)
    {
        var longAgo = nowUtc - LongWait;
        var pending = await db.ChangeRequests
            .Where(r => r.Status == ChangeRequestStatus.Pending && !r.Direct
                        && (r.NotifiedAtUtc == null || r.CreatedAtUtc <= longAgo))
            .OrderBy(r => r.CreatedAtUtc)
            .ToListAsync(ct);
        if (pending.Count == 0)
            return new ApprovalNotifyOutcome(0, 0);

        var scope = await ApprovalScope.LoadAsync(db, nowUtc, ct);
        var everyone = pending.SelectMany(r => scope.ApproversOf(r.LocationId)).Distinct().ToList();
        var active = (await db.Employees.AsNoTracking()
                .Where(e => everyone.Contains(e.Id) && e.IsActive)
                .Select(e => e.Id)
                .ToListAsync(ct))
            .ToHashSet();
        List<Guid> ApproversOf(ChangeRequest r) =>
            scope.ApproversOf(r.LocationId).Where(p => p != r.RequestedByEmployeeId && active.Contains(p)).ToList();

        // 1. A new request: its approvers hear about it once.
        var announced = 0;
        foreach (var request in pending.Where(r => r.NotifiedAtUtc == null))
        {
            request.NotifiedAtUtc = nowUtc;
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Decided between the read and the mark. Nobody needs to hear about it now.
                db.Entry(request).State = EntityState.Detached;
                continue;
            }
            announced++;
            var to = ApproversOf(request);
            if (!pushOn || to.Count == 0)
                continue;
            var branch = scope.Branch(request.LocationId)?.Name;
            await SendAsync(notifier, to, $"Yeni müraciət R-{request.Number}",
                $"Yeni işçi: {ChangeRequestDesk.SubjectOf(request)}" + (branch is null ? "" : $" — {branch}"), logger, ct);
        }

        // 2. Waiting more than 24 hours: once a day, one message per approver with how many wait for them.
        var local = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, timeZone);
        var today = DateOnly.FromDateTime(local);
        if (local.Hour < ReminderHour)
            return new ApprovalNotifyOutcome(announced, 0);
        var stale = pending
            .Where(r => db.Entry(r).State != EntityState.Detached
                        && r.CreatedAtUtc <= longAgo && (r.RemindedOn is null || r.RemindedOn < today))
            .ToList();
        if (stale.Count == 0)
            return new ApprovalNotifyOutcome(announced, 0);
        foreach (var request in stale)
            request.RemindedOn = today;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // One of them was decided meanwhile. The next pass reads them again.
            db.ChangeTracker.Clear();
            return new ApprovalNotifyOutcome(announced, 0);
        }
        var perApprover = stale
            .SelectMany(r => ApproversOf(r).Select(p => (Approver: p, Request: r)))
            .GroupBy(x => x.Approver)
            .ToList();
        if (pushOn)
            foreach (var group in perApprover)
                await SendAsync(notifier, [group.Key], "Gözləyən müraciətlər",
                    $"24 saatdan çox gözləyən müraciət: {group.Count()}", logger, ct);
        return new ApprovalNotifyOutcome(announced, stale.Count);
    }

    /// <summary>A push that fails is logged and forgotten. The request is the record, and the queue shows it.</summary>
    private static async Task SendAsync(
        IPushNotifier notifier, IReadOnlyCollection<Guid> to, string title, string body, ILogger logger, CancellationToken ct)
    {
        try
        {
            await notifier.NotifyEmployeesAsync(to, title, body, "/admin", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Approval push to {Count} approver(s) failed", to.Count);
        }
    }
}
