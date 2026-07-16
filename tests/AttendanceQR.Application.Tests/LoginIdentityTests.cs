using AttendanceQR.Api;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Pins the login lockout key. This is a security control, not a convenience: the key decides whose
/// 5-attempt budget an attempt spends. Keying on raw input gave each SPELLING of one phone number its
/// own budget, and a number can be spelled unlimited ways — which made a 4-digit PIN brute-forceable
/// despite the lockout. The two invariants below are what stop that.
/// </summary>
public class LoginIdentityTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    [Theory]
    // The bypass, pinned: every spelling the account lookup treats as one account must spend ONE budget.
    [InlineData("0501234567")]
    [InlineData("+994 50 123 45 67")]
    [InlineData("994501234567")]
    [InlineData("+994501234567")]
    [InlineData("(050) 123-45-67")]
    [InlineData("050 123 45 67")]
    [InlineData("  0501234567  ")]
    public void Every_spelling_of_one_number_shares_one_lockout_key(string spelling)
    {
        var canonical = LoginIdentity.LockoutKey(TenantA, "0501234567");
        Assert.Equal(canonical, LoginIdentity.LockoutKey(TenantA, spelling));
    }

    [Fact]
    public void Different_numbers_get_different_keys()
    {
        // The flip side: over-merging would let one user lock out another.
        Assert.NotEqual(
            LoginIdentity.LockoutKey(TenantA, "0501234567"),
            LoginIdentity.LockoutKey(TenantA, "0551234567"));
    }

    [Fact]
    public void The_same_number_in_two_tenants_is_two_keys()
    {
        // (TenantId, PhoneNumber) is unique per tenant, not globally — two companies can each have an
        // 0501234567. A shared key would let failures against one company's user lock out another's.
        Assert.NotEqual(
            LoginIdentity.LockoutKey(TenantA, "0501234567"),
            LoginIdentity.LockoutKey(TenantB, "0501234567"));
    }

    [Theory]
    [InlineData("admin@bms.az", "ADMIN@BMS.AZ")]
    [InlineData("admin@bms.az", "  Admin@Bms.Az  ")]
    public void Emails_are_case_and_whitespace_insensitive(string a, string b)
        => Assert.Equal(LoginIdentity.LockoutKey(TenantA, a), LoginIdentity.LockoutKey(TenantA, b));

    [Fact]
    public void Email_and_phone_are_different_keys()
    {
        Assert.NotEqual(
            LoginIdentity.LockoutKey(TenantA, "admin@bms.az"),
            LoginIdentity.LockoutKey(TenantA, "0501234567"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_identifier_is_a_key_not_a_crash(string? identifier)
    {
        // Login used to hand the store a possibly-null string, which threw inside Normalize → a 500
        // on a malformed request. Empty input now buckets together like any other key.
        var key = LoginIdentity.LockoutKey(TenantA, identifier);
        Assert.NotNull(key);
        Assert.Equal(LoginIdentity.LockoutKey(TenantA, ""), key);
    }

    [Fact]
    public void Key_is_scoped_by_tenant_even_for_an_empty_identifier()
        => Assert.NotEqual(LoginIdentity.LockoutKey(TenantA, ""), LoginIdentity.LockoutKey(TenantB, ""));

    [Fact]
    public void Admin_reset_clears_the_key_login_would_have_locked()
    {
        // AdminController.ResetPin clears using the STORED phone (already normalized); an employee
        // locked out having typed it with a leading zero must be cleared by that call.
        var typedAtLogin = LoginIdentity.LockoutKey(TenantA, "0501234567");
        var clearedByAdmin = LoginIdentity.LockoutKey(TenantA, "501234567"); // as stored
        Assert.Equal(typedAtLogin, clearedByAdmin);
    }
}
