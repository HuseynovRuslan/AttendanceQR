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
    ScanBlockedOnDevice = 8,

    // An unrecognised device scanned from inside the geofence and was adopted without an admin
    // approving it. Also the counter behind the per-employee auto-bind rate limit.
    DeviceAutoBound = 9,

    // An admin killed a bound device. It will not be re-adopted automatically.
    DeviceBindingRevoked = 10
}
