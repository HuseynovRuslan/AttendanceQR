using System.Security.Claims;
using AttendanceQR.Api;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Support and view sessions no longer run out (c098fa1), so the operator behind one has to still be an
/// operator on every request — taking an id off the allowlist is the only way left to end what that
/// operator has open.
/// </summary>
public class ImpersonationAllowlistTests
{
    private static readonly Guid Operator = Guid.Parse("00000000-0000-0000-0000-00000000a001");
    private static readonly Guid FormerOperator = Guid.Parse("00000000-0000-0000-0000-00000000a002");
    private static readonly Guid[] Allowlist = [Operator];

    private static ClaimsPrincipal Session(string? imp, bool readOnly = false)
    {
        var claims = new List<Claim> { new("sub", Guid.NewGuid().ToString()), new("role", "Admin") };
        if (imp is not null) claims.Add(new Claim("imp", imp));
        if (readOnly) claims.Add(new Claim("ro", "1"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "jwt"));
    }

    [Fact]
    public void An_ordinary_login_is_never_asked()
    {
        // Every employee and admin session in the product. An empty allowlist must not touch them either.
        Assert.True(ImpersonationAllowlist.StillAllowed(Session(imp: null), Allowlist));
        Assert.True(ImpersonationAllowlist.StillAllowed(Session(imp: null), []));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_session_opened_by_a_listed_operator_carries_on(bool readOnly)
    {
        Assert.True(ImpersonationAllowlist.StillAllowed(Session(Operator.ToString(), readOnly), Allowlist));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Taking_the_operator_off_the_list_ends_both_kinds_of_session(bool readOnly)
    {
        Assert.False(ImpersonationAllowlist.StillAllowed(Session(FormerOperator.ToString(), readOnly), Allowlist));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    public void A_borrowed_session_that_names_nobody_is_refused(string imp)
    {
        // Fail closed: a malformed claim is not read as "no impersonation".
        Assert.False(ImpersonationAllowlist.StillAllowed(Session(imp), Allowlist));
    }
}
