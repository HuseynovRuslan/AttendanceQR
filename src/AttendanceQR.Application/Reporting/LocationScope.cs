using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Application.Reporting;

/// <summary>A DailySummary query already narrowed to what the caller may see, plus an access verdict.</summary>
public sealed record ScopedSummaryQuery(ReportAccess Access, IQueryable<DailySummary> Query, string Label);

/// <summary>
/// The single authority for report/export visibility — reused by both the JSON summary and the
/// Excel export so scope can never be bypassed by one path. Role decides the rule:
/// Admin = all (optionally one location), Manager = only their ManagedLocations, Employee = only self.
/// </summary>
public static class LocationScope
{
    public static async Task<ScopedSummaryQuery> ApplyLocationScopeAsync(
        AppDbContext db,
        IQueryable<DailySummary> baseQuery,
        Guid requesterId,
        EmployeeRole role,
        Guid? requestedLocationId,
        CancellationToken ct)
    {
        switch (role)
        {
            case EmployeeRole.Admin:
                return requestedLocationId is Guid adminLoc
                    ? new ScopedSummaryQuery(ReportAccess.Allowed, baseQuery.Where(s => s.LocationId == adminLoc), $"Location {adminLoc}")
                    : new ScopedSummaryQuery(ReportAccess.Allowed, baseQuery, "All locations");

            case EmployeeRole.Manager:
                var managed = await db.ManagedLocations
                    .Where(m => m.EmployeeId == requesterId)
                    .Select(m => m.LocationId)
                    .ToListAsync(ct);

                if (requestedLocationId is Guid reqLoc)
                {
                    // Asking for a specific location they don't manage → forbidden.
                    if (!managed.Contains(reqLoc))
                        return new ScopedSummaryQuery(ReportAccess.Forbidden, baseQuery.Where(_ => false), "Forbidden");
                    return new ScopedSummaryQuery(ReportAccess.Allowed, baseQuery.Where(s => s.LocationId == reqLoc), $"Location {reqLoc}");
                }

                // No location filter → everything across their managed locations.
                return new ScopedSummaryQuery(
                    ReportAccess.Allowed,
                    baseQuery.Where(s => managed.Contains(s.LocationId)),
                    "Managed locations");

            default: // Employee — strictly their own summaries, regardless of any locationId passed.
                return new ScopedSummaryQuery(
                    ReportAccess.Allowed,
                    baseQuery.Where(s => s.EmployeeId == requesterId),
                    "Own records");
        }
    }
}
