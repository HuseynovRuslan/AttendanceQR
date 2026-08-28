using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Services;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The rule that decides whether a phone may become a brigade's shared handset.
///
/// "One phone, one employee" is what stops a colleague clocking in for someone who did not turn up.
/// The shared-phone feature gives that up for everybody carried on the device — which is the right
/// trade for the ~260 workers who own no phone, and the wrong one as a capability every employee has
/// by default. Before this, any of them could assemble a shared handset and no screen showed it: every
/// limit in the device rules is per EMPLOYEE (three devices each, three adoptions a month each), so
/// nothing bounded the other axis at all.
///
/// Two things are pinned here. A device is adopted for a SECOND employee only if that employee has
/// been granted it, and one handset cannot accumulate the company. Both are checked at ADOPTION, so an
/// arrangement already in use is never broken mid-morning — that matters because the person it would
/// strand is standing at a poster.
///
/// The two controls that actually catch a colleague scanning for someone else are untouched and are
/// the stronger pair: the scan must happen inside the geofence, and the selfie must be that person's
/// face.
/// </summary>
public class SharedDeviceRuleTests
{
    private static readonly int Cap = new DeviceBindingOptions().MaxAccountsPerDevice;

    /// <summary>The real rule the scan path calls — not a copy of it. A test that restated the logic
    /// here would keep passing after the rule changed, which is the failure mode worth avoiding on a
    /// control this one.</summary>
    private static DeviceBindingRules.ShareRefusal? Decide(bool canShare, int othersOnDevice, int cap)
        => DeviceBindingRules.MayJoinDevice(canShare, othersOnDevice, cap);

    [Fact]
    public void An_employees_own_new_phone_is_adopted_as_before()
    {
        // The ordinary case, and the one that must not have become harder: nobody else is on the
        // device, so none of this applies — not even for an employee without the permission.
        Assert.Null(Decide(canShare: false, othersOnDevice: 0, Cap));
    }

    [Fact]
    public void Somebody_elses_phone_is_refused_without_the_permission()
    {
        // The hole this closes. Any employee could put a colleague's account on their own handset,
        // needing that colleague's PIN once and never again afterwards.
        Assert.Equal(DeviceBindingRules.ShareRefusal.NotAllowed, Decide(canShare: false, othersOnDevice: 1, Cap));
    }

    [Fact]
    public void A_worker_the_company_granted_it_to_may_join_a_brigade_phone()
    {
        Assert.Null(Decide(canShare: true, othersOnDevice: 1, Cap));
        Assert.Null(Decide(canShare: true, othersOnDevice: 14, Cap));
    }

    [Fact]
    public void One_handset_cannot_accumulate_the_company()
    {
        // The axis nothing bounded. The client caps its saved profiles at 60, but that lives in
        // localStorage and so is not a control; this is the server's ceiling.
        Assert.Null(Decide(canShare: true, othersOnDevice: Cap - 1, Cap));
        Assert.Equal(DeviceBindingRules.ShareRefusal.AccountLimit, Decide(canShare: true, othersOnDevice: Cap, Cap));
    }

    [Fact]
    public void The_permission_is_checked_before_the_ceiling()
    {
        // Both refusals are actionable but the fixes are opposite — grant a permission, or split the
        // brigade across two phones. Reporting the wrong one sends an admin down the wrong path.
        Assert.Equal(DeviceBindingRules.ShareRefusal.NotAllowed, Decide(canShare: false, othersOnDevice: 5, 2));
    }

    [Fact]
    public void The_default_is_off()
    {
        // Said out loud because the default IS the fix. A capability every employee holds is not a
        // decision the company made.
        Assert.False(new Employee { FullName = "x", PasswordHash = "h" }.CanShareDevice);
    }

    [Fact]
    public void The_ceiling_is_configurable_and_defaults_to_a_workable_brigade()
    {
        // Twenty is an operational number, not a security one: past about fifteen people a shared
        // phone stops working anyway, because each scan costs half a minute of queue at the poster
        // and the brigade spends a quarter of an hour there, twice a day.
        Assert.Equal(20, new DeviceBindingOptions().MaxAccountsPerDevice);
    }
}
