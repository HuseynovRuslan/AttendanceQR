using AttendanceQR.Api;
using AttendanceQR.Infrastructure.Security;
using Microsoft.Extensions.Caching.Memory;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The brute-force guard end to end: <see cref="LoginIdentity"/> keys + the real store. The headline
/// test is <see cref="Spelling_a_number_differently_does_not_buy_more_attempts"/> — that is the actual
/// bypass, reproduced against the store rather than argued about.
/// </summary>
public class LoginLockoutStoreTests
{
    private const int MaxAttempts = 5;
    private static readonly Guid Tenant = Guid.NewGuid();

    private static MemoryCacheLoginLockoutStore NewStore()
        => new(new MemoryCache(new MemoryCacheOptions()));

    private static string Key(string identifier) => LoginIdentity.LockoutKey(Tenant, identifier);

    [Fact]
    public void Not_locked_out_before_any_failure()
        => Assert.False(NewStore().IsLockedOut(Key("0501234567")));

    [Fact]
    public void Locks_out_on_the_fifth_failure()
    {
        var store = NewStore();
        var key = Key("0501234567");

        for (var i = 1; i < MaxAttempts; i++)
        {
            store.RecordFailure(key);
            Assert.False(store.IsLockedOut(key)); // still has budget
        }

        store.RecordFailure(key);
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

        foreach (var spelling in spellings)
            store.RecordFailure(Key(spelling));

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
            store.RecordFailure(key);
        store.RecordSuccess(key);

        // Back to a full budget: four more failures must not lock.
        for (var i = 0; i < MaxAttempts - 1; i++)
            store.RecordFailure(key);
        Assert.False(store.IsLockedOut(key));
    }

    [Fact]
    public void An_admin_pin_reset_clears_a_lockout_however_the_employee_typed_their_number()
    {
        // The employee locked themselves out typing "+994 50 123 45 67"; ResetPin clears using the
        // stored, normalized number. They must be able to log in immediately after.
        var store = NewStore();
        for (var i = 0; i < MaxAttempts; i++)
            store.RecordFailure(Key("+994 50 123 45 67"));
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

        for (var i = 0; i < MaxAttempts; i++)
            store.RecordFailure(LoginIdentity.LockoutKey(Tenant, "0501234567"));

        Assert.True(store.IsLockedOut(LoginIdentity.LockoutKey(Tenant, "0501234567")));
        Assert.False(store.IsLockedOut(LoginIdentity.LockoutKey(other, "0501234567")));
    }

    [Fact]
    public void Two_different_employees_have_independent_budgets()
    {
        var store = NewStore();
        for (var i = 0; i < MaxAttempts; i++)
            store.RecordFailure(Key("0501234567"));

        Assert.True(store.IsLockedOut(Key("0501234567")));
        Assert.False(store.IsLockedOut(Key("0557654321")));
    }
}
