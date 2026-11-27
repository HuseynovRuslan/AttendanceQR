using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Jobs;

/// <summary>
/// Once, at startup: binds every push subscription registered before subscriptions followed the session
/// (<see cref="Domain.Entities.PushSubscription.Unbound"/>) to its employee's current TokenVersion.
///
/// Why at startup and not only at send time: a row adopted lazily on its first push takes whatever version
/// the employee has by then — a PIN reset in between would be adopted, not obeyed. Run right after the
/// migration, the adoption happens before any reset could. Send time (PushSessionBinding) still adopts a
/// row this ever misses, so a failure here costs nobody their reminders.
///
/// One tenant scope at a time, like every other job: no IgnoreQueryFilters, no raw SQL. Idempotent — once
/// every row is bound, each later start finds nothing to do.
/// </summary>
public sealed class PushSubscriptionAdoption : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PushSubscriptionAdoption> _logger;

    public PushSubscriptionAdoption(IServiceScopeFactory scopeFactory, ILogger<PushSubscriptionAdoption> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var adopted = await AdoptAllAsync(stoppingToken);
            if (adopted > 0)
                _logger.LogInformation("Push: bound {Count} earlier subscription(s) to their current session", adopted);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Push: binding earlier subscriptions failed; send time will bind them instead");
        }
    }

    /// <summary>Every tenant, in its own scope. Internal so a test can run it against a seeded database.</summary>
    internal async Task<int> AdoptAllAsync(CancellationToken ct)
    {
        List<Guid> tenantIds;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Suspended companies too: their rows are bound to a session like anyone's, and a company that
            // comes back must not find its devices adopted only then.
            tenantIds = await db.Tenants.Select(t => t.Id).ToListAsync(ct);
        }

        var total = 0;
        foreach (var tenantId in tenantIds)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                scope.ServiceProvider.GetRequiredService<ITenantContext>().Resolve(tenantId);
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                total += await PushSessionBinding.AdoptUnboundAsync(db, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Push: binding earlier subscriptions failed for tenant {TenantId}", tenantId);
            }
        }
        return total;
    }
}
