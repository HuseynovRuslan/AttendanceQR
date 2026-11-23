namespace AttendanceQR.IntegrationTests.Infrastructure;

/// <summary>
/// A test that needs a real PostgreSQL 16.
///
/// Where the database comes from, in order:
///   1. <c>QRLOG_IT_CONNECTION</c> — a connection string to a server you already run (any database you can
///      CREATE DATABASE on; the tests make and drop their own databases and never touch existing ones);
///   2. otherwise Testcontainers starts <c>postgres:16-alpine</c> (override with <c>QRLOG_IT_PG_IMAGE</c>,
///      e.g. a registry mirror) on the local Docker daemon.
///
/// With neither available the test is SKIPPED, so a laptop without Docker can still run <c>dotnet test</c>.
/// CI sets <c>QRLOG_IT_REQUIRED=1</c>, which turns "no database" into a failure: a suite that silently skips
/// the only tests that see the real database proves nothing.
/// </summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (!PostgresAvailability.CanRun(out var reason))
            Skip = reason;
    }
}

/// <summary>The theory flavour of <see cref="PostgresFactAttribute"/>.</summary>
public sealed class PostgresTheoryAttribute : TheoryAttribute
{
    public PostgresTheoryAttribute()
    {
        if (!PostgresAvailability.CanRun(out var reason))
            Skip = reason;
    }
}

internal static class PostgresAvailability
{
    public static bool Required =>
        Environment.GetEnvironmentVariable("QRLOG_IT_REQUIRED") is "1" or "true";

    public static bool CanRun(out string reason)
    {
        reason = string.Empty;
        // Required: never skip — let the fixture fail loudly if it cannot reach a database.
        if (Required)
            return true;
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("QRLOG_IT_CONNECTION")))
            return true;
        if (DockerLooksAvailable())
            return true;

        reason = "No PostgreSQL for integration tests: set QRLOG_IT_CONNECTION, or start Docker. "
               + "(CI sets QRLOG_IT_REQUIRED=1, where this is a failure, not a skip.)";
        return false;
    }

    private static bool DockerLooksAvailable()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOCKER_HOST")))
            return true;
        if (OperatingSystem.IsWindows())
            return File.Exists(@"\\.\pipe\docker_engine");
        return File.Exists("/var/run/docker.sock")
               || File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".docker/run/docker.sock"));
    }
}
