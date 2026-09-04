using AttendanceQR.Domain;
using AttendanceQR.Domain.Entities;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The «Qrup rəhbəri» role — the head of the CUSTOMER's group of companies.
///
/// Added 2026-09-02 because a customer asked to see all of his companies on one screen, and the only
/// cross-company view lived inside the OPERATOR console. Every existing role would have answered that
/// request by also handing him the platform's own business: what each company pays, and — for Support
/// — the ability to act as anyone in any of them. This role is the narrow answer: his attendance, and
/// nothing of ours.
///
/// The whole point is what it CANNOT do, so that is what is pinned here.
/// </summary>
public class GroupHeadRoleTests
{
    [Theory]
    [InlineData(OperatorPermission.ManageTenants)]
    [InlineData(OperatorPermission.Billing)]
    [InlineData(OperatorPermission.ManageUsers)]
    [InlineData(OperatorPermission.Impersonate)]
    [InlineData(OperatorPermission.Announce)]
    [InlineData(OperatorPermission.ManageTeam)]
    public void A_group_head_may_change_nothing(OperatorPermission perm)
    {
        Assert.False(OperatorAccess.Allows(OperatorRoleType.GroupHead, perm));
    }

    [Fact]
    public void And_may_not_see_the_platforms_own_business()
    {
        // Prices, invoices, the operator team, the operator audit log. He is the customer: what he
        // pays us is his, what everyone pays us is not his to read from inside our console.
        Assert.False(OperatorAccess.Allows(OperatorRoleType.GroupHead, OperatorPermission.ViewBusiness));
    }

    [Fact]
    public void A_group_head_holds_no_permissions_at_all()
    {
        // Belt and braces: if a permission is ever added to the enum, it must not silently fall to
        // this role. An empty list is the invariant, not a coincidence of today's enum.
        Assert.Empty(OperatorAccess.PermissionsFor(OperatorRoleType.GroupHead));
    }

    [Fact]
    public void The_business_gate_does_not_take_anything_from_the_existing_roles()
    {
        // ViewBusiness was introduced to exclude ONE role. Everybody who could read billing, the team
        // and the audit log yesterday must still read them today.
        Assert.True(OperatorAccess.Allows(OperatorRoleType.Full, OperatorPermission.ViewBusiness));
        Assert.True(OperatorAccess.Allows(OperatorRoleType.Support, OperatorPermission.ViewBusiness));
        Assert.True(OperatorAccess.Allows(OperatorRoleType.Billing, OperatorPermission.ViewBusiness));
    }

    [Fact]
    public void Support_and_Billing_keep_exactly_the_powers_they_had()
    {
        Assert.True(OperatorAccess.Allows(OperatorRoleType.Support, OperatorPermission.ManageUsers));
        Assert.True(OperatorAccess.Allows(OperatorRoleType.Support, OperatorPermission.Impersonate));
        Assert.False(OperatorAccess.Allows(OperatorRoleType.Support, OperatorPermission.ManageTenants));

        Assert.True(OperatorAccess.Allows(OperatorRoleType.Billing, OperatorPermission.Billing));
        Assert.False(OperatorAccess.Allows(OperatorRoleType.Billing, OperatorPermission.Impersonate));
    }

    [Fact]
    public void A_group_head_can_never_be_the_last_operator_standing()
    {
        // Only Full holds ManageTeam. Demoting the last Full to GroupHead would leave nobody able to
        // set roles again — a lockout only a redeploy could undo. The existing guard must count this
        // new role as "not Full", which it does by not being Full; this pins that it is checked.
        var onlyFull = Guid.NewGuid();
        Assert.True(OperatorAccess.WouldLeaveNoFull(
            operatorIds: new[] { onlyFull },
            profiledRoles: new Dictionary<Guid, OperatorRoleType>(),
            changingId: onlyFull,
            newRole: OperatorRoleType.GroupHead));

        // …but with a second, untouched operator (who defaults to Full) the demotion is safe.
        var other = Guid.NewGuid();
        Assert.False(OperatorAccess.WouldLeaveNoFull(
            operatorIds: new[] { onlyFull, other },
            profiledRoles: new Dictionary<Guid, OperatorRoleType>(),
            changingId: onlyFull,
            newRole: OperatorRoleType.GroupHead));
    }
}
