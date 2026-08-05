using AttendanceQR.Domain.Entities;

namespace AttendanceQR.Infrastructure.Security;

public interface IJwtService
{
    /// <summary>Issues a signed login JWT for the given employee.</summary>
    string GenerateToken(Employee employee);

    /// <summary>Issues a SHORT-LIVED login JWT for <paramref name="employee"/> on behalf of a
    /// super-admin who is impersonating them for support. Carries an "imp" claim (the operator's
    /// employee id) so the session is identifiable as impersonation, and expires in
    /// <paramref name="expiryMinutes"/> so it cannot linger like a normal ~100-year login.</summary>
    string GenerateImpersonationToken(Employee employee, Guid impersonatedBy, int expiryMinutes);
}
