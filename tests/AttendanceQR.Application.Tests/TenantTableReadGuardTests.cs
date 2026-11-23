using System.Text.RegularExpressions;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// SEC-01 guard: the few tables that carry NO tenant query filter must never be read "for the current company"
/// without naming the company.
///
/// Fail-closed multi-tenancy protects every <c>ITenantScoped</c> entity automatically. It does nothing for the
/// platform's own registry — <c>Tenants</c>, <c>TenantInvoices</c>, <c>OperatorProfiles</c>,
/// <c>SuperAdminAuditLogs</c> — because those are global by design (the operator console reads all of them).
/// So a customer-facing read of them is safe only if the statement itself says which company it means.
/// <c>TenantBillingController</c> once read <c>_db.Tenants.FirstOrDefaultAsync()</c> and showed every other
/// company the plan, price and invoices of whichever company PostgreSQL had stored first. This test reads the
/// source so that pattern cannot come back in another controller.
///
/// It is a text check, deliberately simple: a statement (from the DbSet to the next ';') must contain an
/// explicit key comparison. Places that legitimately read every company — the operator console, the
/// per-tenant job loops, startup seeding — are listed by file, each with its reason.
/// </summary>
public class TenantTableReadGuardTests
{
    /// <summary>Files allowed to read the registry across companies, with why.</summary>
    private static readonly (string PathFragment, string Why)[] CrossTenantReaders =
    {
        ("Controllers/SuperAdminController", "operator console — gated by the employee-id allowlist"),
        ("/Jobs/", "background jobs iterate every active company and resolve each one"),
        ("DailyReport/DailyReportJob.cs", "background job iterating every active company"),
        ("AttendanceQR.Api/Program.cs", "startup seeding and the per-request token check (keyed by the token's tenant)"),
        ("Services/Access/AccessSeeder.cs", "startup reconcile across every company"),
        ("Persistence/TenantPurge.cs", "operator-only tenant deletion"),
    };

    private static readonly (string DbSet, Regex RequiredKey)[] UnfilteredRegistry =
    {
        ("Tenants", new Regex(@"\b(Id|Slug)\s*==", RegexOptions.Compiled)),
        ("TenantInvoices", new Regex(@"\bTenantId\s*==", RegexOptions.Compiled)),
        // Only the operator console has any business reading these at all.
        ("OperatorProfiles", new Regex(@"(?!)", RegexOptions.Compiled)),
        ("SuperAdminAuditLogs", new Regex(@"(?!)", RegexOptions.Compiled)),
    };

    [Fact]
    public void Unfiltered_registry_tables_are_never_read_without_naming_the_company()
    {
        var src = Path.Combine(RepoRoot(), "src");
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, file).Replace('\\', '/');
            if (rel.Contains("/bin/") || rel.Contains("/obj/") || rel.Contains("/Migrations/"))
                continue;
            if (CrossTenantReaders.Any(a => ("/" + rel).Contains(a.PathFragment)))
                continue;

            var text = File.ReadAllText(file);
            foreach (var (dbSet, requiredKey) in UnfilteredRegistry)
            {
                foreach (Match m in Regex.Matches(text, $@"\.{dbSet}\b(?!\s*=>)"))
                {
                    // The DbSet declaration itself (AppDbContext) and plain writes are not reads.
                    var lineStart = text.LastIndexOf('\n', m.Index) + 1;
                    var lineEnd = text.IndexOf('\n', m.Index);
                    var line = text[lineStart..(lineEnd < 0 ? text.Length : lineEnd)];
                    if (line.TrimStart().StartsWith("//"))
                        continue;
                    if (line.Contains("DbSet<"))
                        continue;

                    var end = text.IndexOf(';', m.Index);
                    var statement = text[m.Index..(end < 0 ? text.Length : end)];
                    if (Regex.IsMatch(statement, $@"^\.{dbSet}\s*\.(Add|AddRange|Remove|RemoveRange|Attach)\s*\("))
                        continue;
                    if (requiredKey.IsMatch(statement))
                        continue;

                    var lineNo = text[..m.Index].Count(c => c == '\n') + 1;
                    offenders.Add($"src/{rel}:{lineNo}: .{dbSet} read without naming the company → {line.Trim()}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "These reads of an UNFILTERED registry table do not say which company they mean, so on PostgreSQL they "
            + "answer with whichever company's row comes first. Add an explicit Id/TenantId predicate (from the "
            + "token's tenant), or — if the read is genuinely cross-tenant and operator-only — add the file to "
            + "CrossTenantReaders with its reason:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void The_guard_still_sees_the_controller_it_was_written_for()
    {
        // A guard that silently stops scanning (moved file, renamed folder) would pass forever. Pin that it
        // actually reaches the customer billing controller and finds its keyed read there.
        var file = Path.Combine(RepoRoot(), "src", "AttendanceQR.Api", "Controllers", "TenantBillingController.cs");
        Assert.True(File.Exists(file), $"expected {file}");
        var text = File.ReadAllText(file);
        Assert.Matches(@"\.Tenants\s*\.FirstOrDefaultAsync\(\s*t\s*=>\s*t\.Id\s*==", text);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "AttendanceQR.slnx")))
                return dir.FullName;
        throw new InvalidOperationException("Could not find the repository root (AttendanceQR.slnx) above " + AppContext.BaseDirectory);
    }
}
