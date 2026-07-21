using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Infrastructure.Services;

/// <summary>The ballot settings actually in force for the current tenant, and the window they imply.
/// One place computes this so the voting screen, the vote itself and the winner job can never
/// disagree about whether the ballot is open.</summary>
public sealed record EffectiveVoteSettings(
    bool Enabled,
    int OpenDaysBeforeEnd,
    DateOnly? ManualFrom,
    DateOnly? ManualTo,
    int MinCandidates,
    int MinVotesToDecide)
{
    /// <summary>The window for the month containing <paramref name="today"/>. Explicit dates win when
    /// both are set; otherwise it's the last N days of that month.</summary>
    public (bool Open, DateOnly From, DateOnly To) Window(DateOnly today)
    {
        if (ManualFrom is { } f && ManualTo is { } t)
            return (today >= f && today <= t, f, t);

        var to = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
        var from = to.AddDays(-(Math.Max(1, OpenDaysBeforeEnd) - 1));
        return (today >= from && today <= to, from, to);
    }
}

public interface IVoteSettingsProvider
{
    Task<EffectiveVoteSettings> GetAsync(CancellationToken ct = default);
}

public sealed class VoteSettingsProvider : IVoteSettingsProvider
{
    private readonly AppDbContext _db;
    private readonly VoteOptions _fallback;

    public VoteSettingsProvider(AppDbContext db, VoteOptions fallback)
    {
        _db = db;
        _fallback = fallback;
    }

    /// <summary>Reads the tenant's row, falling back to the server defaults until an admin saves one —
    /// so the feature works out of the box for a company that never opens the settings.</summary>
    public async Task<EffectiveVoteSettings> GetAsync(CancellationToken ct = default)
    {
        var row = await _db.VoteSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        return row is null
            ? new EffectiveVoteSettings(true, _fallback.OpenDaysBeforeEnd, null, null, _fallback.MinCandidates, _fallback.MinVotesToDecide)
            : new EffectiveVoteSettings(row.Enabled, row.OpenDaysBeforeEnd, row.ManualFrom, row.ManualTo, row.MinCandidates, row.MinVotesToDecide);
    }
}
