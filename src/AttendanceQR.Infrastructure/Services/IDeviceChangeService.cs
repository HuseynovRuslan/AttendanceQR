namespace AttendanceQR.Infrastructure.Services;

/// <summary>Result of an employee requesting a device change.</summary>
public enum RequestDeviceChangeOutcome
{
    Created,
    PendingExists
}

/// <summary>Result of an admin reviewing (approving/rejecting) a device change request.</summary>
public enum ReviewDeviceChangeOutcome
{
    Done,
    NotFound,
    AlreadyReviewed
}

public sealed record RequestDeviceChangeResult(RequestDeviceChangeOutcome Outcome, Guid? RequestId);

/// <summary>A pending request enriched for admin review — requester name and current vs new device.</summary>
public sealed record PendingDeviceChangeDto(
    Guid RequestId,
    Guid EmployeeId,
    string EmployeeName,
    string? CurrentDeviceFingerprint,
    string NewDeviceFingerprint,
    DateTime RequestedAtUtc);

/// <summary>
/// Business logic for the device-change flow. Kept out of the controllers, which only translate
/// HTTP &lt;-&gt; these calls.
/// </summary>
public interface IDeviceChangeService
{
    Task<RequestDeviceChangeResult> RequestAsync(
        Guid employeeId, string newDeviceFingerprint, string? ip, CancellationToken ct = default);

    Task<IReadOnlyList<PendingDeviceChangeDto>> GetPendingAsync(CancellationToken ct = default);

    Task<ReviewDeviceChangeOutcome> ApproveAsync(
        Guid requestId, Guid adminId, string? ip, CancellationToken ct = default);

    Task<ReviewDeviceChangeOutcome> RejectAsync(
        Guid requestId, Guid adminId, string? ip, CancellationToken ct = default);
}
