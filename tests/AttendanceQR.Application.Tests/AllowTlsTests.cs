using AttendanceQR.Api.Controllers;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Caddy asks this endpoint before obtaining a certificate for a hostname it has never seen. A "yes"
/// therefore spends OUR Let's Encrypt account on that name, and afterwards our frontend is served
/// under it.
///
/// It used to answer on `domain.Split('.')[0]` alone — the first label, with nothing checked after
/// it. So `bax.attacker.example` read as the slug "bax", matched a real tenant, and earned a genuine
/// certificate for a hostname we do not own. These pin the rule that replaced it: exactly one label
/// in front of qrlog.az, nothing else.
/// </summary>
public class AllowTlsTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    private static (TenantController Controller, AppDbContext Db) Build(bool tenantActive = true)
    {
        var tenant = new TenantContext();
        tenant.Resolve(TenantId);
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"allow-tls-{Guid.NewGuid()}").Options, tenant);
        db.Tenants.Add(new Tenant
        {
            Id = TenantId, Name = "Bakı Abadlıq", Slug = "bax", DisplayName = "Bakı Abadlıq",
            IsActive = tenantActive,
        });
        db.SaveChanges();

        var controller = new TenantController(db, tenant)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        return (controller, db);
    }

    private static async Task<bool> Allows(string? domain, bool tenantActive = true)
    {
        var (controller, db) = Build(tenantActive);
        using (db)
            return await controller.AllowTls(domain) is OkResult or OkObjectResult;
    }

    [Theory]
    [InlineData("bax.qrlog.az")]      // a real tenant
    [InlineData("BAX.QRLOG.AZ")]      // DNS is case-insensitive
    [InlineData("bax.qrlog.az.")]     // a trailing dot is a legal absolute name
    [InlineData("admin.qrlog.az")]    // operator console — served, not a tenant
    [InlineData("app.qrlog.az")]      // native app host — same
    public async Task Our_own_hosts_are_allowed(string domain)
        => Assert.True(await Allows(domain));

    [Theory]
    // The bug: the first label matched a tenant and everything after it was ignored.
    [InlineData("bax.attacker.example")]
    [InlineData("bax.qrlog.az.attacker.example")]
    [InlineData("admin.attacker.example")]
    // Not one label in front of our domain.
    [InlineData("a.bax.qrlog.az")]
    [InlineData("qrlog.az")]
    [InlineData(".qrlog.az")]
    // A name that simply is not ours.
    [InlineData("bax.qrlog.com")]
    [InlineData("notqrlog.az")]
    public async Task Everything_else_is_refused(string domain)
        => Assert.False(await Allows(domain));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Nothing_is_refused(string? domain)
        => Assert.False(await Allows(domain));

    [Fact]
    public async Task An_unknown_slug_on_our_domain_gets_no_certificate()
        => Assert.False(await Allows("nosuchtenant.qrlog.az"));

    [Fact]
    public async Task A_suspended_tenant_gets_no_certificate()
        => Assert.False(await Allows("bax.qrlog.az", tenantActive: false));
}
