using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AttendanceQR.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by the EF Core tools (e.g. `dotnet ef migrations add`).
/// It lets migrations be created against this project without depending on the Api
/// startup project. `migrations add` does not connect to the database — the connection
/// string only needs to identify the provider.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        const string connectionString =
            "Host=localhost;Database=attendanceqr;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AppDbContext(options);
    }
}
