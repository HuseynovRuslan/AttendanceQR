using AttendanceQR.Domain.Entities;

namespace AttendanceQR.Domain;

/// <summary>
/// The two shift templates every company starts with.
///
/// They lived only in the startup seeder, which backfills any tenant that has none. That is fine for a
/// company created by a redeploy and wrong for one created from the console: the shift picker is the
/// first thing the operator opens after the branch, and until the next backend restart it was empty —
/// with nothing on screen to say why, and no way to make it fill. Both callers build them from here so
/// the two can never drift apart.
///
/// WorkDaysMask 126 is Monday–Saturday: bit 0 is Sunday, so 0111 1110.
/// </summary>
public static class DefaultSchedules
{
    public static IEnumerable<Schedule> For(Guid tenantId) =>
    [
        new Schedule
        {
            TenantId = tenantId, Name = "Gündüz", ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
            LateThresholdMinutes = 15, WorkDaysMask = 126,
        },
        // End before start: an overnight shift, which the calculator resolves with a noon pivot.
        new Schedule
        {
            TenantId = tenantId, Name = "Gecə növbəsi", ShiftStart = new TimeOnly(22, 0), ShiftEnd = new TimeOnly(6, 0),
            LateThresholdMinutes = 15, WorkDaysMask = 126,
        },
    ];
}
