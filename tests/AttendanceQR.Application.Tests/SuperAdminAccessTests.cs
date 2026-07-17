using AttendanceQR.Api.Multitenancy;
using AttendanceQR.Application.Common;

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
