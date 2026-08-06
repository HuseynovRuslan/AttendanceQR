using AttendanceQR.Api.Multitenancy;
using AttendanceQR.Application.Common;
using AttendanceQR.Domain;
using AttendanceQR.Domain.Entities;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The super-admin allowlist is the only thing standing between one company's admin and every other
/// company's data, so its parsing has to fail CLOSED — a malformed entry must narrow the list, never
/// widen it.
/// </summary>
public class SuperAdminAccessTests
{
    private static AppOptions With(string ids) => new() { SuperAdminEmployeeIds = ids };

    [Fact]
    public void Nobody_is_a_super_admin_by_default()
    {
        // The screen does not exist until someone is named. An empty list must never read as "all".
        Assert.Empty(new AppOptions().SuperAdminIdList());
        Assert.Empty(With("").SuperAdminIdList());
        Assert.Empty(With("   ").SuperAdminIdList());
    }

    [Fact]
    public void Reads_one_id()
    {
        var id = Guid.NewGuid();
        Assert.Equal([id], With(id.ToString()).SuperAdminIdList());
    }

    [Fact]
    public void Reads_several_and_tolerates_spacing()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        Assert.Equal([a, b], With($" {a} ,  {b} ").SuperAdminIdList());
    }

    [Fact]
    public void Garbage_is_dropped_not_treated_as_a_wildcard()
    {
        var real = Guid.NewGuid();
        var list = With($"not-a-guid,{real},,also-junk").SuperAdminIdList();
        Assert.Equal([real], list);
    }

    [Fact]
    public void A_list_of_only_garbage_grants_nothing()
        => Assert.Empty(With("admin@bms.az,yes,true,*").SuperAdminIdList());
}

/// <summary>
/// Only Full holds ManageTeam, so if a role change ever empties the Full set nobody can manage roles
/// again — a lockout only a redeploy could undo. <see cref="OperatorAccess.WouldLeaveNoFull"/> is the
/// guard; the team endpoint calls it under an advisory lock so two concurrent demotions can't both slip
/// past. The trap it must not fall into: an allowlisted operator with NO profile row is Full by default,
/// so counting profile rows alone would miscount.
/// </summary>
public class LastFullOperatorGuardTests
{
    private static bool WouldLeaveNoFull(
        IEnumerable<Guid> ids,
        Dictionary<Guid, OperatorRoleType> profiled,
        Guid changing,
        OperatorRoleType newRole)
        => OperatorAccess.WouldLeaveNoFull(ids, profiled, changing, newRole);

    [Fact]
    public void Demoting_the_last_Full_operator_is_refused()
    {
        var only = Guid.NewGuid();
        // One operator, currently Full (a profile row). Demoting them to Support leaves nobody Full.
        Assert.True(WouldLeaveNoFull(
            [only], new() { [only] = OperatorRoleType.Full }, only, OperatorRoleType.Support));
    }

    [Fact]
    public void Default_Full_no_row_operator_counts_as_Full()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        // b has no profile row → Full by default. Demoting a to Support still leaves b Full, so it's fine.
        Assert.False(WouldLeaveNoFull(
            [a, b], new() { [a] = OperatorRoleType.Full }, a, OperatorRoleType.Support));
    }

    [Fact]
    public void Concurrent_mutual_demotion_is_caught_once_the_first_has_committed()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        // A already committed B → Support. Now B's transaction tries to demote A → Support. With the lock
        // serializing them, B reads B=Support and computes A→Support: nobody Full → refuse.
        Assert.True(WouldLeaveNoFull(
            [a, b], new() { [b] = OperatorRoleType.Support }, a, OperatorRoleType.Support));
    }

    [Fact]
    public void A_demotion_that_still_leaves_another_Full_is_allowed()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        // Three operators; c stays Full, so demoting b to Billing is fine.
        Assert.False(WouldLeaveNoFull(
            [a, b, c],
            new() { [a] = OperatorRoleType.Full, [b] = OperatorRoleType.Full, [c] = OperatorRoleType.Full },
            b, OperatorRoleType.Billing));
    }

    [Fact]
    public void Promoting_to_Full_is_never_a_lockout()
    {
        var a = Guid.NewGuid();
        // Even from an all-narrowed state, setting someone TO Full obviously leaves a Full operator.
        Assert.False(WouldLeaveNoFull(
            [a], new() { [a] = OperatorRoleType.Support }, a, OperatorRoleType.Full));
    }
}

/// <summary>
/// A tenant slug becomes a hostname. Creating one on a label the resolver refuses would leave a
/// company that exists in the database and answers nothing — every request from its subdomain
/// rejected as unattributable, with no clue why.
/// </summary>
public class ReservedSlugTests
{
    [Theory]
    [InlineData("api")]      // the backend itself
    [InlineData("www")]
    [InlineData("qrlog")]    // the apex/marketing site
    [InlineData("localhost")]
    [InlineData("127")]
    public void Labels_the_resolver_refuses_are_reserved(string label)
        => Assert.True(TenantSlug.IsReservedLabel(label));

    [Theory]
    [InlineData("API")]
    [InlineData("Www")]
    public void Reserved_is_case_insensitive(string label)
        => Assert.True(TenantSlug.IsReservedLabel(label));

    [Theory]
    [InlineData("bax")]
    [InlineData("ecaf")]
    [InlineData("cleanfix")]
    [InlineData("yenisirket")]
    public void A_real_company_slug_is_not_reserved(string label)
        => Assert.False(TenantSlug.IsReservedLabel(label));
}
