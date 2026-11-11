namespace AttendanceQR.Domain.Enums;

/// <summary>One line of a change request's history. Stored as int — never renumber.</summary>
public enum ChangeRequestEventKind
{
    Created = 0,
    Approved = 1,
    Rejected = 2,
    Cancelled = 3,

    /// <summary>The approved change was written — for a new person, the account exists from this line on.</summary>
    Applied = 4,

    /// <summary>An approver pressed «Təsdiqlə» but the change could not be written (the phone number is taken
    /// since, the branch was closed). Nothing was written, the request is still Pending, the reason is here.</summary>
    ApplyRefused = 5,

    /// <summary>Somebody who may decide did the change themselves, without a request. Recorded so the history of an
    /// approval area is in one place.</summary>
    Direct = 6,
}
