using AttendanceQR.Infrastructure.Multitenancy;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Pins the tenant isolation boundary's fail-closed rule. This used to default to Bakı Abadlıq when
/// nothing resolved it, which was safe only while they were the sole tenant; with several live
/// tenants an unresolved request would read — and, via SaveChanges stamping, WRITE — another
/// company's rows. If someone ever reintroduces a fallback default, this test fails.
/// </summary>
public class TenantContextTests
{
    [Fact]
    public void Starts_unresolved()
        => Assert.False(new TenantContext().IsResolved);

    [Fact]
    public void Reading_the_tenant_before_it_is_resolved_throws_rather_than_defaulting()
    {
        var ctx = new TenantContext();
        var ex = Assert.Throws<InvalidOperationException>(() => ctx.TenantId);
        Assert.Contains("not resolved", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_makes_the_tenant_readable()
    {
        var id = Guid.NewGuid();
        var ctx = new TenantContext();
        ctx.Resolve(id);
        Assert.True(ctx.IsResolved);
        Assert.Equal(id, ctx.TenantId);
    }

    [Fact]
    public void Resolve_is_last_write_wins()
    {
        // The startup scope relies on this: it resolves to the original tenant, then TenantSeed
        // re-points the same scope at the tenant it just created.
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var ctx = new TenantContext();
        ctx.Resolve(first);
        ctx.Resolve(second);
        Assert.Equal(second, ctx.TenantId);
    }
}
