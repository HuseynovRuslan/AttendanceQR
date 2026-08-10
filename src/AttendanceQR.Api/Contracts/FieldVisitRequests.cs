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
    string? Note = null,
    // YOXLAMA SİYAHISI — the lines the worker will tick ("süpür", "zibili boşalt", "sula"). Null or
    // empty = a visit with no checklist, exactly as before. The server trims, drops blanks, de-dupes
    // case-insensitively, truncates each to 120 chars and keeps the first 10 — it never 400s on this.
    IReadOnlyList<string>? Checklist = null);

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

/// <summary>
/// A worker leaves the site. Coordinates are OPTIONAL: a basement or an underground car park must not
/// stop someone recording that they left — an unknown position FLAGS the visit for the manager,
/// exactly as a far GPS does. They used to be required, and the app returned early without posting
/// anything when GPS failed, so the visit stayed open and the day read as zero hours.
/// </summary>
public record FieldCheckOutRequest(
    [Range(-90d, 90d)] double? Latitude = null,
    [Range(-180d, 180d)] double? Longitude = null,
    string? PhotoBase64 = null,
    // Final tick state — the ABSOLUTE set of done item ids, not a delta. Reconciles ticks whose own
    // POST never reached the server, and replaying the same check-out changes nothing.
    IReadOnlyList<Guid>? DoneItemIds = null);

/// <summary>A worker ticks or unticks one checklist line. Absolute state, never a toggle.</summary>
public record SetChecklistItemRequest(bool IsDone);

/// <summary>İŞ ŞƏKLİ upload — a JPEG data URL of the WORK, taken with the rear camera and sent AFTER
/// the check-out is already recorded, so a failed upload can never cost the worker their departure.</summary>
public record WorkPhotoRequest(string PhotoBase64);
