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
    public void The_selfie_is_gated_by_SCOPE_and_the_gate_is_never_absent()
    {
        // The rule changed on 2026-09-04: a manager may look again, at the people they manage.
        //
        // What is pinned is not WHICH roles pass — that is a judgement the owner has now reversed
        // twice — but that a gate exists at all. The scope check is now the only one, so its absence
        // would silently open every company's faces to every authenticated caller, and this test is
        // what would fail first.
        var body = Method(
            Source("src/AttendanceQR.Api/Controllers/AttendanceController.cs"),
            "public async Task<IActionResult> PhotoUrl(Guid recordId)");

        Assert.Contains("CanAccessEmployeeAsync", body);
        Assert.Contains("Status403Forbidden", body);
    }

    [Fact]
    public void An_employee_cannot_open_anyone_elses_selfie()
    {
        // The narrowest thing the scope rule must still do, and the one nobody would notice breaking
        // until it had: a plain employee reaching another person's record id.
        var body = Method(
            Source("src/AttendanceQR.Api/Controllers/AttendanceController.cs"),
            "public async Task<IActionResult> PhotoUrl(Guid recordId)");

        // The check is passed the CALLER's id and role — not the record's — which is what makes it
        // answer "may this person see that person" rather than "does this record exist".
        Assert.Contains("CanAccessEmployeeAsync(_db, requesterId, role, record.EmployeeId", body);
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
