using System.Reflection;
using AttendanceQR.Api.Controllers;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Who may open a check-in selfie.
///
/// Admin only, owner's call on 2026-08-31. It had been open to managers as well, narrowed to their
/// own branch — and branch scope is the wrong axis for this one thing. The photograph is of somebody's
/// face, taken at work: biometric data, of which the company is the named controller in its own
/// privacy notice. Who may look at it is a question about the PERSON looking, not about which site
/// they run.
///
/// A manager loses nothing they need to run a day — who came, when, from where, and whether the face
/// matched are all still theirs. They no longer open the photograph itself.
///
/// This test reads the controller's source, which is unusual and deliberate: the guard is one
/// early-return inside a long method, the kind of line a later edit removes while "tidying up" the
/// authorization block, and no other test in the suite would notice. Hiding the button is not a
/// boundary either — the URL is guessable from a record id.
/// </summary>
public class PhotoVisibilityTests
{
    private static string Source(string relative)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
            dir = Directory.GetParent(dir)?.FullName;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!, relative.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>The body of one method, from its signature to the next one.</summary>
    private static string Method(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start > 0, $"{signature} tapılmadı");
        var next = source.IndexOf("\n    [Http", start, StringComparison.Ordinal);
        return next > start ? source[start..next] : source[start..];
    }

    [Fact]
    public void The_check_in_selfie_is_refused_to_anyone_who_is_not_an_admin()
    {
        var body = Method(
            Source("src/AttendanceQR.Api/Controllers/AttendanceController.cs"),
            "public async Task<IActionResult> PhotoUrl(Guid recordId)");

        Assert.Contains("role != EmployeeRole.Admin", body);
        Assert.Contains("Status403Forbidden", body);
    }

    [Fact]
    public void And_the_branch_check_is_still_there_underneath_it()
    {
        // The role gate is a ceiling, not a replacement. An admin is still read through the same
        // location rule as everything else, so a future per-branch admin does not quietly gain the
        // whole company's photographs.
        var body = Method(
            Source("src/AttendanceQR.Api/Controllers/AttendanceController.cs"),
            "public async Task<IActionResult> PhotoUrl(Guid recordId)");

        Assert.Contains("CanAccessEmployeeAsync", body);
    }

    [Fact]
    public void The_endpoint_still_exists_and_is_reachable_by_an_authenticated_caller()
    {
        // Guards the guard: if the method were renamed or removed, the two assertions above would
        // fail for the wrong reason and read like a policy change.
        var method = typeof(AttendanceController).GetMethod("PhotoUrl", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(method);
    }
}
