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
/// <param name="RecentNewDevices">New devices the employee arrived with in the last thirty days,
/// adopted or approved — see <see cref="DeviceBindingAllowance.RecentNewDevicesAsync"/>.</param>
/// <param name="AutoBindLimitReached">The poster has stopped adopting new devices for this employee —
/// the same test the scan refuses on. This request is then not a new phone but the allowance running
/// out, and approving it is what starts the allowance again.</param>
public sealed record PendingDeviceChangeDto(
    Guid RequestId,
    Guid EmployeeId,
    string EmployeeName,
    string? CurrentDeviceFingerprint,
    string NewDeviceFingerprint,
    DateTime RequestedAtUtc,
    int RecentNewDevices,
    bool AutoBindLimitReached);

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
