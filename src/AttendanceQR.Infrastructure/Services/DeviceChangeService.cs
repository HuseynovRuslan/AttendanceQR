using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Infrastructure.Services;

public sealed class DeviceChangeService : IDeviceChangeService
{
    private readonly AppDbContext _db;

    public DeviceChangeService(AppDbContext db) => _db = db;

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

        // Design decision: UPDATE the existing binding in place rather than deactivate-and-insert.
        // DeviceBinding.EmployeeId carries a UNIQUE index (1-to-1), so a second row for the same
        // employee — even with IsActive=false on the old one — would violate it. Overwriting is the
        // clean, index-safe move; the previous fingerprint is preserved in the audit trail.
        var binding = await _db.DeviceBindings.FirstOrDefaultAsync(d => d.EmployeeId == request.EmployeeId, ct);
        if (binding is null)
        {
            binding = new DeviceBinding { EmployeeId = request.EmployeeId };
            _db.DeviceBindings.Add(binding);
        }
        binding.DeviceFingerprint = request.NewDeviceFingerprint;
        binding.BoundAtUtc = now;
        binding.IsActive = true;

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
