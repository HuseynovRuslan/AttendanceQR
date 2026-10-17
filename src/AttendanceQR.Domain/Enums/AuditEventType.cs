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
    CheckInOutsideFence = 17,

    // A manager handed another account a new temporary PIN or changed its login number. Written against
    // the account that CHANGED, naming the manager in the reason: since 2026-09-14 a manager may do this
    // to a fellow manager anywhere in the company, and a PIN they reset is a PIN they know — so the row
    // is what turns a quiet sign-in as somebody else into a question with a name and a time on it.
    CredentialChangedByManager = 18,

    // A branch's managers (or, without one, the company's admins) were pushed about this employee:
    // refused for a reason they can fix on the spot, or asking for a new phone. Written against the
    // employee, the reason «Kind|reached/recipients who». It is also what keeps the alert to once per
    // person, problem and day — see StaffAlertDispatcher.
    StaffAlertSent = 19,

    // A structure was created, renamed, archived or restored. Written against the employee who did it;
    // the reason names the structure and what changed.
    StructureChanged = 20,

    // A branch was put into a structure, moved to another, or taken out of one. Written against the
    // employee who did it, the reason «Branch: Old → New». Who sees a branch follows its structure once
    // structure-wide access exists, so every move is on the record.
    LocationStructureChanged = 21,

    // A role was given to somebody over an area, or taken away (Phase 3, AdminAccessController). Written
    // against the person who RECEIVED or LOST it; the reason «By {who}: {role} @ {area}». Who could do what
    // on a given day is answerable from these lines and the assignment rows, which are revoked, never deleted.
    AccessGranted = 22,
    AccessRevoked = 23,

    // A role itself was created, edited, archived or restored — its permission set is everybody's who holds
    // it, so every change is on the record. Written against the employee who did it.
    AccessRoleChanged = 24,

    // Somebody corrected their OWN day — allowed only where the company had nobody else who could (see
    // AttendanceSelfCorrection). Its own event, never folded into RecordEditedByAdmin: «who fixed their own pay»
    // is one search. The reason «{kind}: {before} → {after}. Səbəb: {why}»; the same times are kept for good in
    // AttendanceSelfCorrections.
    AttendanceSelfCorrected = 25,

    // A self-correction that waited for approval was approved (and applied) or rejected. Written against the
    // person whose day it is; the reason names the approver and their reason.
    SelfCorrectionDecided = 26,

    // The company switched «a self-correction waits for a second approval» on or off. Written against who did it.
    SelfCorrectionSettingChanged = 27
}
