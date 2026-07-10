using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Infrastructure.Services;

public sealed class DeviceChangeService : IDeviceChangeService
{
    private readonly AppDbContext _db;
    private readonly DeviceBindingOptions _options;

    public DeviceChangeService(AppDbContext db, DeviceBindingOptions options)
    {
        _db = db;
        _options = options;
    }

    public async Task<RequestDeviceChangeResult> RequestAsync(
        Guid employeeId, string newDeviceFingerprint, string? ip, CancellationToken ct = default)
    {
        // One open request at a time — blocks duplicate submissions.
        var hasPending = await _db.DeviceChangeRequests
            .AnyAsync(r => r.EmployeeId == employeeId && r.Status == DeviceChangeStatus.Pending, ct);
        if (hasPending)
            return new RequestDeviceChangeResult(RequestDeviceChangeOutcome.PendingExists, null);

        var request = new DeviceChangeRequest
        {
            EmployeeId = employeeId,
            NewDeviceFingerprint = newDeviceFingerprint,
            Status = DeviceChangeStatus.Pending
        };
        _db.DeviceChangeRequests.Add(request);
        _db.AuditLogs.Add(new AuditLog
        {
            EmployeeId = employeeId,
            EventType = AuditEventType.DeviceChangeRequested,
            IpAddress = ip
        });
        await _db.SaveChangesAsync(ct);

        return new RequestDeviceChangeResult(RequestDeviceChangeOutcome.Created, request.Id);
    }

    public async Task<IReadOnlyList<PendingDeviceChangeDto>> GetPendingAsync(CancellationToken ct = default)
    {
        // Current (old) fingerprint is the employee's active binding, if any.
        var rows = await (
            from r in _db.DeviceChangeRequests
            where r.Status == DeviceChangeStatus.Pending
            join e in _db.Employees on r.EmployeeId equals e.Id
            orderby r.RequestedAtUtc
            select new PendingDeviceChangeDto(
                r.Id,
                r.EmployeeId,
                e.FullName,
                _db.DeviceBindings
                    .Where(d => d.EmployeeId == r.EmployeeId && d.IsActive)
                    .OrderByDescending(d => d.LastSeenAtUtc)
                    .Select(d => d.DeviceFingerprint)
                    .FirstOrDefault(),
                r.NewDeviceFingerprint,
                r.RequestedAtUtc))
            .ToListAsync(ct);

        return rows;
    }

    public async Task<ReviewDeviceChangeOutcome> ApproveAsync(
        Guid requestId, Guid adminId, string? ip, CancellationToken ct = default)
    {
        var request = await _db.DeviceChangeRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null)
            return ReviewDeviceChangeOutcome.NotFound;
        if (request.Status != DeviceChangeStatus.Pending)
            return ReviewDeviceChangeOutcome.AlreadyReviewed;

        var now = DateTime.UtcNow;

        // Approving ADDS the new context rather than overwriting the old one: an employee whose
        // Safari storage was wiped still wants the installed PWA to keep working. The cap and the
        // least-recently-used eviction are the same ones the scan path applies.
        var existing = await _db.DeviceBindings
            .Where(d => d.EmployeeId == request.EmployeeId)
            .ToListAsync(ct);

        var binding = DeviceBindingRules.Bind(
            existing, request.EmployeeId, request.NewDeviceFingerprint, label: null,
            DeviceBindingOrigin.AdminApproval, _options.MaxActiveDevices, now);

        if (!existing.Contains(binding))
            _db.DeviceBindings.Add(binding);

        request.Status = DeviceChangeStatus.Approved;
        request.ReviewedByEmployeeId = adminId;
        request.ReviewedAtUtc = now;

        _db.AuditLogs.Add(new AuditLog
        {
            EmployeeId = request.EmployeeId,
            EventType = AuditEventType.DeviceChangeApproved,
            IpAddress = ip
        });

        // All of the above persist in a single SaveChanges — EF wraps it in one DB transaction,
        // so the review is atomic (binding swap + status + audit succeed or fail together).
        await _db.SaveChangesAsync(ct);

        return ReviewDeviceChangeOutcome.Done;
    }

    public async Task<ReviewDeviceChangeOutcome> RejectAsync(
        Guid requestId, Guid adminId, string? ip, CancellationToken ct = default)
    {
        var request = await _db.DeviceChangeRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null)
            return ReviewDeviceChangeOutcome.NotFound;
        if (request.Status != DeviceChangeStatus.Pending)
            return ReviewDeviceChangeOutcome.AlreadyReviewed;

        var now = DateTime.UtcNow;
        request.Status = DeviceChangeStatus.Rejected;
        request.ReviewedByEmployeeId = adminId;
        request.ReviewedAtUtc = now;

        // The existing device binding is deliberately left untouched.
        _db.AuditLogs.Add(new AuditLog
        {
            EmployeeId = request.EmployeeId,
            EventType = AuditEventType.DeviceChangeRejected,
            IpAddress = ip
        });
        await _db.SaveChangesAsync(ct);

        return ReviewDeviceChangeOutcome.Done;
    }
}
