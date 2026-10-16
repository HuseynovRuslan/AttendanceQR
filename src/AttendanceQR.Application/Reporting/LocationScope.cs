using AttendanceQR.Domain.Access;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;

namespace AttendanceQR.Application.Reporting;

/// <summary>A DailySummary query already narrowed to what the caller may see, plus an access verdict.</summary>
public sealed record ScopedSummaryQuery(ReportAccess Access, IQueryable<DailySummary> Query, string Label);

/// <summary>
/// The single authority for report/export visibility — reused by both the JSON summary and the
/// Excel export so scope can never be bypassed by one path. Role decides the rule:
/// Admin = all (optionally one location), Manager = only their ManagedLocations, Employee = only self.
/// In a company switched to Enforced (Phase 5) the permission model decides instead, with the same three
/// answers: «attendance.view» company-wide, over some branches, or nowhere (own records only).
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
        if (db.AuthzObserver is { } observer && await observer.ModeAsync(db, ct) == AuthzMode.Enforced)
        {
            var scope = await LocationScopeRules.ScopeAsync(db, requesterId, role, AccessPermissions.AttendanceView, ct);
            if (scope.Company)
                return requestedLocationId is Guid companyLoc
                    ? new ScopedSummaryQuery(ReportAccess.Allowed, baseQuery.Where(s => s.LocationId == companyLoc), $"Location {companyLoc}")
                    : new ScopedSummaryQuery(ReportAccess.Allowed, baseQuery, "All locations");
            if (scope.Branches.Count > 0)
                return Branches(baseQuery, requesterId, scope.Branches.ToList(), requestedLocationId);
            return new ScopedSummaryQuery(ReportAccess.Allowed, baseQuery.Where(s => s.EmployeeId == requesterId), "Own records");
        }

        switch (role)
        {
            case EmployeeRole.Admin:
                return requestedLocationId is Guid adminLoc
                    ? new ScopedSummaryQuery(ReportAccess.Allowed, baseQuery.Where(s => s.LocationId == adminLoc), $"Location {adminLoc}")
                    : new ScopedSummaryQuery(ReportAccess.Allowed, baseQuery, "All locations");

            case EmployeeRole.Manager:
                // Same managed-location source as the attendance query — one rule, one query. And the
                // same PEOPLE boundary as the live boards: everyone at the branches they manage. It
                // once stopped at Role==Employee here too, which meant the tabel and the reports
                // disagreed with the roster by however many managers a site had.
                var managed = await LocationScopeRules.ManagedLocationIdsAsync(db, requesterId, ct);
                return Branches(baseQuery, requesterId, managed, requestedLocationId);

            default: // Employee — strictly their own summaries, regardless of any locationId passed.
                return new ScopedSummaryQuery(
                    ReportAccess.Allowed,
                    baseQuery.Where(s => s.EmployeeId == requesterId),
                    "Own records");
        }
    }

    /// <summary>A manager's — or, in Enforced, any branch-scoped holder's — view: their branches, or one of them.</summary>
    private static ScopedSummaryQuery Branches(
        IQueryable<DailySummary> baseQuery, Guid requesterId, List<Guid> managed, Guid? requestedLocationId)
    {
        if (requestedLocationId is Guid reqLoc)
        {
            // Asking for a specific location they don't manage → forbidden.
            if (!managed.Contains(reqLoc))
                return new ScopedSummaryQuery(ReportAccess.Forbidden, baseQuery.Where(_ => false), "Forbidden");
            return new ScopedSummaryQuery(
                ReportAccess.Allowed,
                baseQuery.Where(s => s.LocationId == reqLoc),
                $"Location {reqLoc}");
        }

        // No location filter → everything across their managed locations.
        return new ScopedSummaryQuery(
            ReportAccess.Allowed,
            baseQuery.Where(s => s.EmployeeId == requesterId || managed.Contains(s.LocationId)),
            "Managed locations");
    }
}
