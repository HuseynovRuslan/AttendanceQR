using AttendanceQR.Domain.Entities;

namespace AttendanceQR.Api;

/// <summary>
/// Whether a new leave collides with one this person already has.
///
/// Nothing checked this. The same employee could hold a "Məzuniyyət" and an "İstirahət" over the same
/// week, filed by two different people or by the same person twice, and no screen anywhere showed the
/// contradiction — the day's status then came out as whichever row the summary happened to read, and
/// that status is what payroll deducts against. A duplicate is not a small mistake here.
///
/// The rule is the ordinary interval overlap, written once so the admin path and the manager path
/// cannot disagree about what "already off" means.
/// </summary>
public static class LeaveOverlapRule
{
    public static bool Overlaps(LeaveRecord existing, DateOnly from, DateOnly to)
        => existing.FromDate <= to && existing.ToDate >= from;
}
