namespace AttendanceQR.Domain.Enums;

public enum AuditEventType
{
    CheckInSuccess = 0,
    CheckInRejected = 1,
    CheckOutSuccess = 2,
    CheckOutRejected = 3,
    DeviceChangeRequested = 4,
    DeviceChangeApproved = 5,
    DeviceChangeRejected = 6,
    RecordEditedByAdmin = 7,

    // The scan never reached the server: the browser refused to give the phone's position, so there
    // was no QR to validate. Self-reported by the client, otherwise these employees are invisible.
    ScanBlockedOnDevice = 8
}
