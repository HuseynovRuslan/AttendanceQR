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
    RecordEditedByAdmin = 7
}
