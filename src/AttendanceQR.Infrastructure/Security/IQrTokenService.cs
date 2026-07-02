namespace AttendanceQR.Infrastructure.Security;

public interface IQrTokenService
{
    string Generate(Guid locationId);

    QrTokenValidationResult Validate(string token);
}
