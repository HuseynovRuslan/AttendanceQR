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
}
