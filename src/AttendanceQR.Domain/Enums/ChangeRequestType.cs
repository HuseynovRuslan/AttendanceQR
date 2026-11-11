namespace AttendanceQR.Domain.Enums;

/// <summary>
/// What a change request asks for. Stored as int — never renumber, never reuse a retired value.
///
/// Phase 1 implements one kind end to end. The other eight of docs/tesdiq-sistemi-plan.md §5 get their own value
/// when their apply path is written, not before: a type the server cannot apply must not be creatable.
/// Zero is left unused so an unset value can never pass for a real kind.
/// </summary>
public enum ChangeRequestType
{
    /// <summary>Add a new person to a branch — «Yeni işçi əlavə edilməsi».</summary>
    EmployeeCreate = 1,
}
