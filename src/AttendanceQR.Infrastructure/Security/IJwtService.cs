using AttendanceQR.Domain.Entities;

namespace AttendanceQR.Infrastructure.Security;

public interface IJwtService
{
    /// <summary>Issues a signed login JWT for the given employee.</summary>
    string GenerateToken(Employee employee);

    /// <summary>Issues a login JWT for <paramref name="employee"/> on behalf of a super-admin who is
    /// impersonating them for support. Carries an "imp" claim (the operator's employee id) so the
    /// session is identifiable as impersonation.
    ///
    /// It lives as long as any other login. It used to expire after sixty minutes so it could not
    /// linger; on 03.10.2026 the owner chose otherwise — support is needed often, and a session that
    /// died every hour cost a fresh sign-in each time. It ends when the operator presses «Çıx», or when
    /// the borrowed account's own sessions are retired (its tv).</summary>
    /// <param name="readOnly">Mints a «baxış rejimi» session: carries an extra "ro" claim that
    /// ViewOnlyBoundary uses to refuse every mutating request. For the customer's group head, who may
    /// read all of his companies and change none of them.</param>
    string GenerateImpersonationToken(Employee employee, Guid impersonatedBy, bool readOnly = false);
}
