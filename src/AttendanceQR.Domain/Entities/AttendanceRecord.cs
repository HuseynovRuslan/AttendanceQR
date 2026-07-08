using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Domain.Entities;

public class AttendanceRecord
{
    public AttendanceRecord()
    {
        Id = Guid.NewGuid();
    }

    public Guid Id { get; set; }

    public Guid EmployeeId { get; set; }

    public Guid LocationId { get; set; }

    // Per-day uniqueness key — see (EmployeeId, AttendanceDate) unique index.
    public DateOnly AttendanceDate { get; set; }

    public DateTime? CheckInAtUtc { get; set; }

    public DateTime? CheckOutAtUtc { get; set; }

    public AttendanceStatus Status { get; set; }

    // Photo audit: object key (in MinIO, not the DB) of the selfie captured at check-in, plus when
    // it was taken. Null when the client sent no photo (camera denied / capture failed) — check-in
    // is never blocked on the photo. See MinioPhotoStorageService for the key layout.
    public string? CheckInPhotoKey { get; set; }

    public DateTime? CheckInPhotoTakenAtUtc { get; set; }
}
