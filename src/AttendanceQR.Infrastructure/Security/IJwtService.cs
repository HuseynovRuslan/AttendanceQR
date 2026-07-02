using AttendanceQR.Domain.Entities;

namespace AttendanceQR.Infrastructure.Security;

public interface IJwtService
{
    /// <summary>Issues a signed login JWT for the given employee.</summary>
    string GenerateToken(Employee employee);
}
