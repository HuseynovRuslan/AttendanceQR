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
    DeviceBindingRevoked = 10,

    // An employee who forgot their PIN asked (anonymously, from the login screen) for a reset.
    PinResetRequested = 11,

    // An admin reset an employee's PIN off the back of a request (or dismissed a bogus one).
    PinResetResolved = 12,

    // An employee reset their OWN PIN with no admin, proving identity by face match from a bound device.
    PinResetSelfService = 13,

    // A platform operator borrowed this company's admin account for a support/setup session. Written
    // into the COMPANY's own audit, because the operator console's log is invisible from inside the
    // tenant and every edit the borrowed session makes is recorded under the admin's own id — without
    // this row the customer has no way of knowing the platform was ever in their account.
    ImpersonationStarted = 14,

    // An admin issued fresh temporary PINs to a group at once — the onboarding list, reissued. Worth
    // its own row rather than a hundred PinResetResolved ones: it invalidates every PIN already handed
    // out to those people, so "why did my PIN stop working" has an answer with a time on it.
    BulkPinReset = 15,

    // A branch's geofence was moved — its centre, its radius, or both. The one edit in the product
    // that changes who is able to clock in from where, and the reason a manager correcting their own
    // site is safe to allow: it is written down, with the distance, every time.
    LocationMoved = 16,

    // A scan that fell OUTSIDE the branch's radius and was recorded anyway, because that branch has
    // its geofence switched off (Location.RequireGeofence = false). Not a rejection and not a
    // success worth burying: it is a MEASUREMENT. A site nobody has ever managed to clock in at has
    // no position data to draw a circle from, so the wall comes down, the points are collected, and
    // the radius is then set from what people actually did rather than from a guess on a map.
    // The reason carries "lat,lng,dist" exactly like OutsideRadius, so the same screen can map it.
    CheckInOutsideFence = 17
}
