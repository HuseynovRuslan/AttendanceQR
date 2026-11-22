using AttendanceQR.Application.Common;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.DailyReport;

/// <summary>
/// Once a minute: for every active company, create today's scheduled reports once their time has come,
/// then send whatever is waiting (new ones and retries). A plain BackgroundService like every other job
/// here — no scheduler library. A company that fails is logged and skipped; the others carry on.
/// Inert without a bot token.
/// </summary>
public sealed class DailyReportJob : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(60);
    private readonly IServiceScopeFactory _scopes;
    private readonly DailyReportOptions _options;
    private readonly ILogger<DailyReportJob> _logger;

    public DailyReportJob(IServiceScopeFactory scopes, DailyReportOptions options, ILogger<DailyReportJob> logger)
    {
        _scopes = scopes;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.TelegramConfigured)
        {
            _logger.LogInformation("DailyReportJob: no Telegram bot token configured — daily reports are off.");
            return;
        }
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            // RunOnceAsync keeps its own failures in; this is the backstop for whatever it misses. An
            // exception out of ExecuteAsync ends the job for good, and under the host's default it ended
            // the whole process — every check-in with it — over one failed minute of daily reports.
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DailyReportJob: tick failed; the next one tries again");
            }
            try { await Task.Delay(Tick, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        List<Guid> tenantIds;
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantIds = await db.Tenants.Where(t => t.IsActive).Select(t => t.Id).ToListAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // The database is not answering (a Postgres restart, a full disk). Skipping the minute loses
            // nothing: a scheduled report is still created on any later tick inside its SendWindow, and
            // one waiting to be sent is still waiting.
            _logger.LogError(ex, "DailyReportJob: could not list the companies; skipping this tick");
            return;
        }

        foreach (var tenantId in tenantIds)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                scope.ServiceProvider.GetRequiredService<ITenantContext>().Resolve(tenantId);
                var service = scope.ServiceProvider.GetRequiredService<DailyReportService>();
                var created = await service.CreateScheduledAsync(ct);
                if (created > 0)
                    _logger.LogInformation("DailyReportJob: {Count} scheduled report(s) queued for tenant {Tenant}", created, tenantId);
                var sent = await service.ProcessDueAsync(ct);
                if (sent > 0)
                    _logger.LogInformation("DailyReportJob: {Count} report(s) sent for tenant {Tenant}", sent, tenantId);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DailyReportJob: tenant {Tenant} failed this tick", tenantId);
            }
        }
    }
}

/// <summary>
/// Listens to the bot by long polling — no webhook, so nothing new is exposed to the internet. The only
/// thing it acts on is «/start &lt;company&gt;_&lt;secret&gt;» from a «connect» link: the company is
/// resolved first and the code is looked up through that company's filter, so a code can never bind a
/// chat to another company's recipient. Everything else gets a one-line reply and is ignored.
/// One bot token must be polled by exactly one server — staging needs its own (test) bot.
/// </summary>
public sealed class TelegramUpdatesWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ITelegramClient _telegram;
    private readonly DailyReportOptions _options;
    private readonly ILogger<TelegramUpdatesWorker> _logger;

    public TelegramUpdatesWorker(IServiceScopeFactory scopes, ITelegramClient telegram, DailyReportOptions options,
        ILogger<TelegramUpdatesWorker> logger)
    {
        _scopes = scopes;
        _telegram = telegram;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.TelegramConfigured) return;
        long offset = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var updates = await _telegram.GetUpdatesAsync(offset, 25, stoppingToken);
                foreach (var u in updates)
                {
                    offset = Math.Max(offset, u.UpdateId + 1);
                    if (u.ChatId is long chatId && !string.IsNullOrWhiteSpace(u.Text))
                        await HandleAsync(chatId, u.Text.Trim(), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning("TelegramUpdatesWorker: polling failed ({Type}); retrying in 30 s", ex.GetType().Name);
                try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task HandleAsync(long chatId, string text, CancellationToken ct)
    {
        const string help = "Bu bot QRLog-un rəhbərlik üçün gündəlik davamiyyət hesabatını göndərir. " +
                            "Qoşulmaq üçün şirkətinizin administratorunun verdiyi linkə basın.";
        if (!text.StartsWith("/start", StringComparison.Ordinal))
        {
            await _telegram.SendMessageAsync(chatId, help, ct);
            return;
        }
        var payload = text.Length > 6 ? text[6..].Trim() : string.Empty;
        var sep = payload.IndexOf('_');
        if (sep != 32 || !Guid.TryParseExact(payload[..sep], "N", out var tenantId))
        {
            await _telegram.SendMessageAsync(chatId, help, ct);
            return;
        }
        var secret = payload[(sep + 1)..];

        string reply;
        using (var scope = _scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var active = await db.Tenants.AnyAsync(t => t.Id == tenantId && t.IsActive, ct);
            if (!active)
            {
                reply = "Bu qoşulma linki etibarsızdır.";
            }
            else
            {
                scope.ServiceProvider.GetRequiredService<ITenantContext>().Resolve(tenantId);
                reply = await scope.ServiceProvider.GetRequiredService<DailyReportService>().BindAsync(secret, chatId, ct);
            }
        }
        await _telegram.SendMessageAsync(chatId, reply, ct);
        _logger.LogInformation("TelegramUpdatesWorker: /start handled for tenant {Tenant}", tenantId);
    }
}
