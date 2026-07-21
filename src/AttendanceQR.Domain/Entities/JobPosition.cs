namespace AttendanceQR.Domain.Entities;

/// <summary>
/// A job title the company actually uses, picked from a list when adding an employee.
///
/// Position used to be free text on each employee, and typing it out produced exactly what you would
/// expect: "Layihə Rəhəri", "Layihə rəhbəri" and "Layihə Meneceri" all existed as separate titles for
/// one job. Anything that groups by position — reports, ballot eligibility — silently treated them as
/// three different roles.
///
/// Employee.Position stays a string rather than becoming a foreign key: it is read all over the app
/// (reports, exports, bulk import) and a rename here rewrites those strings, so the catalogue can be
/// introduced without a data migration everywhere. The catalogue is what the UI offers; the string is
/// what everything else keeps reading.
/// </summary>
public class JobPosition : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
