using System.Security.Claims;
using AttendanceQR.Api;
using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The claim names ("sub", "role") now live in exactly one place instead of 25 hand-written reads.
/// These tests are the contract that place has to honour — most importantly that the names match what
/// JwtService actually writes, since a typo there would 401 (or throw) on every request.
/// </summary>
public class ClaimsPrincipalExtensionsTests
{
    private static ClaimsPrincipal User(params (string Type, string Value)[] claims)
        => new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value))));

    [Fact]
    public void EmployeeId_reads_the_sub_claim()
    {
        var id = Guid.NewGuid();
        Assert.Equal(id, User(("sub", id.ToString())).EmployeeId());
    }

    [Fact]
    public void Role_reads_the_role_claim()
        => Assert.Equal(EmployeeRole.Manager, User(("role", "Manager")).Role());

    [Theory]
    [InlineData("Admin", EmployeeRole.Admin)]
    [InlineData("Manager", EmployeeRole.Manager)]
    [InlineData("Employee", EmployeeRole.Employee)]
    public void Every_role_JwtService_can_write_round_trips(string claim, EmployeeRole expected)
    {
        // JwtService writes Role.ToString(); if these two ever drift, authorization silently breaks.
        Assert.Equal(expected, User(("role", claim)).Role());
        Assert.Equal(claim, expected.ToString());
    }

    [Fact]
    public void EmployeeId_throws_when_there_is_no_token()
    {
        // Only reachable by calling this from an [AllowAnonymous] endpoint — a programming error, not
        // a caller error, so it must not masquerade as a 401.
        Assert.Throws<InvalidOperationException>(() => User().EmployeeId());
    }

    [Fact]
    public void EmployeeId_throws_on_a_sub_that_is_not_a_guid()
        => Assert.Throws<InvalidOperationException>(() => User(("sub", "not-a-guid")).EmployeeId());

    [Fact]
    public void Role_throws_when_the_claim_is_missing_or_unknown()
    {
        Assert.Throws<InvalidOperationException>(() => User().Role());
        Assert.Throws<InvalidOperationException>(() => User(("role", "Superuser")).Role());
    }

    [Fact]
    public void The_two_claims_are_read_independently()
    {
        var id = Guid.NewGuid();
        var user = User(("sub", id.ToString()), ("role", "Admin"));
        Assert.Equal(id, user.EmployeeId());
        Assert.Equal(EmployeeRole.Admin, user.Role());
    }
}
