using AttendanceQR.Api.Contracts;
using AttendanceQR.Domain.Entities;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The geofence as a SWITCH — the defaults and the contract around it. The scan behaviour itself is
/// pinned in ScanHandlerTests, which owns the harness that can actually drive a scan.
///
/// «Socar-1 (Aeroport yolu)» is why the switch exists: fourteen staff, a 150 m circle dropped on a
/// stretch of road, four check-ins in the system's whole history and not one rejection — so nobody
/// could say whether the circle was in the right place, and the circle was what stopped anyone
/// producing the evidence either way. With the fence off a scan is recorded wherever it happens and
/// its distance is written to the audit, which the Problems map draws; the radius is then set from
/// real points and the wall goes back up.
///
/// The dangerous half is the DEFAULT. A new bool column takes the CLR default — false — which would
/// have taken the fence down at all twenty-two branches in one deploy, silently, and the fence is the
/// only thing standing between a check-in and somebody's sofa.
/// </summary>
public class GeofenceSwitchTests
{
    [Fact]
    public void A_branch_is_fenced_unless_somebody_says_otherwise()
    {
        Assert.True(new Location().RequireGeofence);
    }

    [Fact]
    public void The_request_contract_treats_silence_as_leave_it_alone_never_as_take_it_down()
    {
        // The EmployeeUpdateRequest wipe, which this product has already been bitten by once: an admin
        // editing a radius from a tab that loaded before these fields existed posts no value for them.
        // Null must mean "unchanged" — for the fence above all, because the alternative is a live site
        // with no location gate and nothing on any screen saying so.
        var partial = new LocationRequest("Aeroport yolu", 40.4, 49.9, 150, "09:00", "18:00", 15, 126);

        Assert.Null(partial.RequireGeofence);
        Assert.Null(partial.QrlessCheckIn);
    }

    [Fact]
    public void Switching_the_fence_off_is_a_deliberate_false_not_an_omission()
    {
        var explicitly = new LocationRequest("Aeroport yolu", 40.4, 49.9, 150, "09:00", "18:00", 15, 126)
        {
            RequireGeofence = false,
        };

        Assert.False(explicitly.RequireGeofence);
    }
}
