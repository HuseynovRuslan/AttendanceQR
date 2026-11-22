using AttendanceQR.Api;
using AttendanceQR.Infrastructure.Security;
using Microsoft.Extensions.Caching.Memory;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The brute-force guard end to end: <see cref="LoginIdentity"/> keys + the real store. The headline
/// test is <see cref="Spelling_a_number_differently_does_not_buy_more_attempts"/> — that is the actual
/// bypass, reproduced against the store rather than argued about. The second is
/// <see cref="Parallel_attempts_never_get_more_than_the_budget"/>: the budget is spent BEFORE the PIN is
/// checked, so a burst of requests in flight at once cannot all see the same unlocked key.
///
/// An attempt that is begun and never followed by <c>RecordSuccess</c> is a failure — that is exactly
/// what the endpoints do with a wrong PIN.
/// </summary>
public class LoginLockoutStoreTests
{
    private const int MaxAttempts = 8;
    private static readonly Guid Tenant = Guid.NewGuid();

    /// <summary>A clock the test moves by hand, so a day of lockouts takes no time at all.</summary>
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 22, 8, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public void Advance(TimeSpan by) => Now += by;
    }

    private static MemoryCacheLoginLockoutStore NewStore(TimeProvider? clock = null)
        => new(new MemoryCache(new MemoryCacheOptions()), clock);

    private static string Key(string identifier) => LoginIdentity.LockoutKey(Tenant, identifier);

    /// <summary>Burn the whole budget with wrong PINs; returns the attempt that set the lock.</summary>
    private static LoginAttempt LockOut(ILoginLockoutStore store, string key)
    {
        LoginAttempt last = default;
        for (var i = 0; i < MaxAttempts; i++)
            last = store.BeginAttempt(key);
        return last;
    }

    [Fact]
    public void Not_locked_out_before_any_failure()
        => Assert.False(NewStore().IsLockedOut(Key("0501234567")));

    [Fact]
    public void Locks_out_on_the_final_allowed_failure()
    {
        var store = NewStore();
        var key = Key("0501234567");

        for (var i = 1; i < MaxAttempts; i++)
        {
            var attempt = store.BeginAttempt(key);
            Assert.True(attempt.Allowed);
            Assert.Equal(MaxAttempts - i, attempt.Remaining); // the "N cəhdiniz qaldı" count, unchanged
            Assert.False(store.IsLockedOut(key));            // still has budget
        }

        var last = store.BeginAttempt(key);
        Assert.True(last.Allowed);           // the eighth guess is still checked...
        Assert.Equal(0, last.Remaining);     // ...but it is the last, and a wrong one answers 429
        Assert.Equal(5, last.LockoutMinutes);
        Assert.True(store.IsLockedOut(key));

        var refused = store.BeginAttempt(key);
        Assert.False(refused.Allowed);
        Assert.Equal(5, refused.LockoutMinutes);
    }

    [Fact]
    public void Parallel_attempts_never_get_more_than_the_budget()
    {
        // THE RACE. The endpoints used to ask IsLockedOut, run PBKDF2, and only then RecordFailure — so
        // two hundred requests fired together all asked while nothing had been recorded yet, and all two
        // hundred PINs were checked. Spending on the way in, under the store's lock, is what bounds them.
        var store = NewStore();
        var key = Key("0501234567");
        var allowed = 0;

        Parallel.For(0, 200, new ParallelOptions { MaxDegreeOfParallelism = 32 }, _ =>
        {
            if (store.BeginAttempt(key).Allowed)
                Interlocked.Increment(ref allowed);
        });

        Assert.Equal(MaxAttempts, allowed);
        Assert.True(store.IsLockedOut(key));
    }

    [Fact]
    public void Spelling_a_number_differently_does_not_buy_more_attempts()
    {
        // THE BYPASS. Five failures spread across five spellings of one number used to be five
        // untouched budgets — 10,000 PINs at 5 free guesses per spelling, and spellings are free.
        var store = NewStore();
        string[] spellings =
        [
            "0501234567", "+994 50 123 45 67", "994501234567", "(050) 123-45-67", "050 123 45 67",
        ];

        // Spread the failures across the different spellings — they share ONE budget, so cycling
        // through them still reaches the lock threshold rather than each buying its own attempts.
        for (var i = 0; i < MaxAttempts; i++)
            store.BeginAttempt(Key(spellings[i % spellings.Length]));

        // Locked under every spelling, because they are all the same key.
        foreach (var spelling in spellings)
            Assert.True(store.IsLockedOut(Key(spelling)), $"'{spelling}' should be locked out");
    }

    [Fact]
    public void A_success_clears_the_budget()
    {
        var store = NewStore();
        var key = Key("0501234567");

        for (var i = 0; i < MaxAttempts - 1; i++)
            store.BeginAttempt(key);
        store.RecordSuccess(key);

        // Back to a full budget: seven more failures must not lock.
        for (var i = 0; i < MaxAttempts - 1; i++)
            store.BeginAttempt(key);
        Assert.False(store.IsLockedOut(key));
    }

    [Fact]
    public void A_right_pin_on_the_last_attempt_lifts_the_lock_it_set()
    {
        // The eighth attempt sets the lock before its PIN is checked. When that PIN turns out right, the
        // employee must be let in and must not find themselves locked on the next screen.
        var store = NewStore();
        var key = Key("0501234567");

        Assert.Equal(0, LockOut(store, key).Remaining);
        store.RecordSuccess(key);

        Assert.False(store.IsLockedOut(key));
        Assert.Equal(MaxAttempts - 1, store.BeginAttempt(key).Remaining);
    }

    [Fact]
    public void Repeated_lockouts_of_one_key_grow_from_5_to_15_to_60_minutes()
    {
        // 8 guesses per 5 minutes is ~2,300 a day — a 4-digit PIN in a few days for anyone patient. The
        // first lock is somebody's thumbs and stays short; the third in one day is nobody's typing.
        var clock = new ManualClock();
        var store = NewStore(clock);
        var key = Key("0501234567");

        Assert.Equal(5, LockOut(store, key).LockoutMinutes);
        clock.Advance(TimeSpan.FromMinutes(4));
        Assert.False(store.BeginAttempt(key).Allowed);   // still locked a minute before the end
        clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(15, LockOut(store, key).LockoutMinutes);
        clock.Advance(TimeSpan.FromMinutes(14));
        var refused = store.BeginAttempt(key);
        Assert.False(refused.Allowed);
        Assert.Equal(1, refused.LockoutMinutes);         // the time LEFT, rounded up
        clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(60, LockOut(store, key).LockoutMinutes);
        clock.Advance(TimeSpan.FromMinutes(60));

        // The ladder tops out rather than locking somebody out for a day.
        Assert.Equal(60, LockOut(store, key).LockoutMinutes);
    }

    [Fact]
    public void A_quiet_day_forgets_the_lockout_history()
    {
        var clock = new ManualClock();
        var store = NewStore(clock);
        var key = Key("0501234567");

        LockOut(store, key);
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(15, LockOut(store, key).LockoutMinutes);

        clock.Advance(TimeSpan.FromHours(25));
        Assert.Equal(5, LockOut(store, key).LockoutMinutes);
    }

    [Fact]
    public void A_success_forgets_the_lockout_history()
    {
        // An admin's PIN reset (or the employee finally getting it right) is a fresh start, not a third
        // strike waiting to happen.
        var clock = new ManualClock();
        var store = NewStore(clock);
        var key = Key("0501234567");

        LockOut(store, key);
        clock.Advance(TimeSpan.FromMinutes(5));
        LockOut(store, key);
        store.RecordSuccess(key);

        Assert.Equal(5, LockOut(store, key).LockoutMinutes);
    }

    [Fact]
    public void Spent_attempts_expire_after_a_quiet_quarter_hour()
    {
        // Seven typos on Monday morning must not leave one attempt for Monday afternoon.
        var clock = new ManualClock();
        var store = NewStore(clock);
        var key = Key("0501234567");

        for (var i = 0; i < MaxAttempts - 1; i++)
            store.BeginAttempt(key);
        clock.Advance(TimeSpan.FromMinutes(16));

        Assert.Equal(MaxAttempts - 1, store.BeginAttempt(key).Remaining);
    }

    [Fact]
    public void An_admin_pin_reset_clears_a_lockout_however_the_employee_typed_their_number()
    {
        // The employee locked themselves out typing "+994 50 123 45 67"; ResetPin clears using the
        // stored, normalized number. They must be able to log in immediately after.
        var store = NewStore();
        LockOut(store, Key("+994 50 123 45 67"));
        Assert.True(store.IsLockedOut(Key("+994 50 123 45 67")));

        store.RecordSuccess(Key("501234567")); // as AdminController.ResetPin does

        Assert.False(store.IsLockedOut(Key("+994 50 123 45 67")));
    }

    [Fact]
    public void Locking_one_tenants_user_does_not_lock_another_tenants()
    {
        // Two companies can each have an 0501234567; one being attacked must not lock the other out.
        var store = NewStore();
        var other = Guid.NewGuid();

        LockOut(store, LoginIdentity.LockoutKey(Tenant, "0501234567"));

        Assert.True(store.IsLockedOut(LoginIdentity.LockoutKey(Tenant, "0501234567")));
        Assert.False(store.IsLockedOut(LoginIdentity.LockoutKey(other, "0501234567")));
    }

    [Fact]
    public void Two_different_employees_have_independent_budgets()
    {
        var store = NewStore();
        LockOut(store, Key("0501234567"));

        Assert.True(store.IsLockedOut(Key("0501234567")));
        Assert.False(store.IsLockedOut(Key("0557654321")));
    }
}
