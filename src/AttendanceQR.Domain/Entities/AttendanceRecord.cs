using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Domain.Entities;

public class AttendanceRecord : ITenantScoped
{
    public AttendanceRecord()
    {
        Id = Guid.NewGuid();
    }

    public Guid Id { get; set; }

    // Multi-tenancy: which company (Tenant) this row belongs to.
    public Guid TenantId { get; set; }

    public Guid EmployeeId { get; set; }

    public Guid LocationId { get; set; }

    // Per-day uniqueness key — see (EmployeeId, AttendanceDate) unique index.
    public DateOnly AttendanceDate { get; set; }

    public DateTime? CheckInAtUtc { get; set; }

    public DateTime? CheckOutAtUtc { get; set; }

    public AttendanceStatus Status { get; set; }

    // Offline check-in: this record was captured while the phone had no connection and synced later.
    // The check-in/out time is the PHONE's clock (trusted only within a sane window — see the Scan
    // handler); SubmittedAtUtc is when the server actually received it. The gap lets an admin audit an
    // offline record. False + null for every normal online scan.
    public bool WasOffline { get; set; }

    public DateTime? SubmittedAtUtc { get; set; }

    // Set once the "you forgot to check out" push went out for this day, so the reminder job never
    // nags the same person twice for the same record. Null = not reminded (yet, or not needed).
    public DateTime? CheckoutReminderSentAtUtc { get; set; }

    // Optional reason the employee gives when they arrive late / leave early (preset chip or free text).
    // Skippable at the scan, so usually null. Surfaced to the admin on the attendance board.
    public string? LateArrivalReason { get; set; }

    public string? EarlyDepartureReason { get; set; }

    // Photo audit: object key (in MinIO, not the DB) of the selfie captured at check-in, plus when
    // it was taken. Null when the client sent no photo (camera denied / capture failed) — check-in
    // is never blocked on the photo. See MinioPhotoStorageService for the key layout.
    public string? CheckInPhotoKey { get; set; }

    public DateTime? CheckInPhotoTakenAtUtc { get; set; }

    // Face audit (AWS Rekognition): similarity of the check-in selfie vs the employee's reference
    // (0–100, null if not compared) and the resulting advisory status. Never affects the check-in
    // itself — only surfaces suspicious records for a manager to review.
    public int? FaceMatchScore { get; set; }

    public FaceMatchStatus FaceMatchStatus { get; set; } = FaceMatchStatus.NotChecked;

    // Where the employee actually stood at check-in — the position their scan sent, which passed the
    // geofence. Kept so the dashboard map can plot people where they really are, not just their site's
    // centre. Null for records made before this was captured, and for an admin-created record (no scan).
    public double? CheckInLatitude { get; set; }

    public double? CheckInLongitude { get; set; }

    // Set when an admin/manager created or changed this record BY HAND — an open-record close, a time
    // correction, an undo-checkout — to the employee id of whoever did it. Null for a normal scan.
    // Surfaced so a manually-entered giriş-çıxış is attributable ("Əl ilə — filankəs") instead of
    // being indistinguishable from a real scan the employee's own pay depends on.
    public Guid? ManualByEmployeeId { get; set; }

    // Set when this day was closed automatically by the employee's own FIELD VISIT check-out, to that
    // visit's id. A worker who spends the day at a site and goes straight home never passes the poster
    // again, so their poster check-in stayed open and the day scored zero hours until an admin noticed
    // it on /admin/open-records and closed it by hand.
    //
    // Deliberately NOT ManualByEmployeeId: nobody touched this by hand. The departure time came from
    // the worker's own check-out, with its GPS and its selfie — better evidence than a typed-in time,
    // and it must not be labelled "Əl ilə — filankəs" as though an admin had invented it. But it is
    // not a poster scan either, and a pay-critical time whose origin cannot be told apart from a real
    // scan is exactly what ManualByEmployeeId exists to prevent. Hence its own column.
    public Guid? ClosedByFieldVisitId { get; set; }
}
