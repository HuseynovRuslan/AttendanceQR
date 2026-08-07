using System.ComponentModel.DataAnnotations;

namespace AttendanceQR.Api.Contracts;

// Coordinate bounds follow ScanRequest's hard-won rules: NO [property:] prefix (MVC validates the
// constructor parameter on a positional record), and DOUBLE literals (-90d) so RangeAttribute picks its
// double overload — an int overload throws OverflowException on infinity, 500ing the request.

/// <summary>A manager assigns a field visit to a worker, optionally pinning a target place. The target
/// coordinates are optional (a fully ad-hoc visit has none); when present the arrival GPS is measured
/// against them.</summary>
public record AssignFieldVisitRequest(
    Guid EmployeeId,
    string? TargetLabel = null,
    [Range(-90d, 90d)] double? TargetLatitude = null,
    [Range(-180d, 180d)] double? TargetLongitude = null,
    int? TargetRadiusMeters = null,
    DateOnly? VisitDate = null,
    string? Note = null);

/// <summary>A worker self-reports an ad-hoc visit — created and checked in at once. An optional label
/// names where they are; a self-report has no pre-set target to measure against.</summary>
public record StartFieldVisitRequest(
    [Range(-90d, 90d)] double Latitude,
    [Range(-180d, 180d)] double Longitude,
    string? TargetLabel = null,
    string? PhotoBase64 = null);

/// <summary>A worker arrives at an assigned visit. Photo optional (like a scan — never blocks).</summary>
public record FieldCheckInRequest(
    [Range(-90d, 90d)] double Latitude,
    [Range(-180d, 180d)] double Longitude,
    string? PhotoBase64 = null);

/// <summary>A worker leaves the site.</summary>
public record FieldCheckOutRequest(
    [Range(-90d, 90d)] double Latitude,
    [Range(-180d, 180d)] double Longitude,
    string? PhotoBase64 = null);
