namespace AttendanceQR.Api.Approvals;

/// <summary>
/// The deployment's switch for the approval flow. It is off unless the environment says otherwise, and the
/// production compose file says <c>false</c>. While it is off, everything works exactly as it did before the flow
/// existed: no branch is in an approval area, the manager desk adds people directly, the request endpoints answer
/// 404, and the notification worker sleeps. Whatever is in the ApprovalAreas table does nothing.
///
/// It is the outer of two keys. With it on, the flow still runs only in the structures and branches the owner
/// switched on one by one (ApprovalArea). There is no company-wide switch.
/// </summary>
public sealed class ApprovalOptions
{
    public const string SectionName = "Approvals";

    public bool Enabled { get; set; }
}
