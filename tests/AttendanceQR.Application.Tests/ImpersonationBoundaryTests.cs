using AttendanceQR.Api;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// What a borrowed session may do, decided once.
///
/// Impersonation was built for looking at a company. It is now how a company gets BUILT — the operator
/// spends an hour inside a customer's admin session laying out branches, staff and shifts — and the
/// admin panel's own sidebar carried a link to the employee shell, which redirects itself to the
/// scanner. One tap there would have written a check-in, an audit row and a selfie under the customer's
/// admin, in the customer's tenant, with the OPERATOR's face in it; and because PhotoUploadWorker
/// promotes a first check-in selfie to the reference photo when an account has none — which is exactly
/// the state of a freshly created admin — that face would have become the customer admin's permanent
/// face-audit baseline, with every later match measured against a stranger.
///
/// The rule is not a list of forbidden endpoints, because a denylist is open until somebody remembers
/// to extend it. Reads pass; writes pass only on the console surfaces. The last test here is the one
/// that matters in a year: an endpoint nobody has written yet is refused.
/// </summary>
public class ImpersonationBoundaryTests
{
    private static bool Allowed(string method, string path)
        => ImpersonationBoundary.IsAllowed(method, new PathString(path));

    [Theory]
    [InlineData("/api/attendance/me/today")]
    [InlineData("/api/reports/today")]
    [InlineData("/api/admin/employees")]
    [InlineData("/api/attendance/scan")]
    public void Reading_is_always_allowed(string path)
    {
        // Support is mostly looking at things, and looking creates nothing. The boundary is about
        // what gets WRITTEN into a customer's company under a borrowed name.
        Assert.True(Allowed("GET", path));
        Assert.True(Allowed("HEAD", path));
    }

    [Theory]
    [InlineData("POST", "/api/admin/employees")]
    [InlineData("PUT", "/api/admin/locations/8f7d6c5b-0000-0000-0000-000000000001")]
    [InlineData("DELETE", "/api/admin/locations/8f7d6c5b-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/admin/positions")]
    [InlineData("POST", "/api/admin/schedules")]
    [InlineData("POST", "/api/admin/non-working-days")]
    [InlineData("POST", "/api/manager/leaves")]
    [InlineData("POST", "/api/reports/export")]
    [InlineData("PUT", "/api/tenant/branding")]
    public void Building_the_company_is_allowed(string method, string path)
    {
        // The whole point: this is the work the operator is there to do.
        Assert.True(Allowed(method, path));
    }

    [Theory]
    [InlineData("/api/attendance/scan")]
    [InlineData("/api/attendance/scan-failure")]
    [InlineData("/api/attendance/me/reference-photo")]
    [InlineData("/api/attendance/me/photo-check")]
    [InlineData("/api/attendance/me/consent")]
    [InlineData("/api/attendance/reason")]
    [InlineData("/api/attendance/missed-checkout")]
    [InlineData("/api/vote")]
    [InlineData("/api/push/subscribe")]
    [InlineData("/api/push/register-native")]
    [InlineData("/api/device-change/request")]
    [InlineData("/api/announcements/00000000-0000-0000-0000-000000000001/ack")]
    public void Acting_as_the_borrowed_employee_is_refused(string path)
    {
        // Each of these files something under the customer's admin as if they had done it: a working
        // day, a face, a consent, a vote, a phone that receives their company's notifications.
        Assert.False(Allowed("POST", path));
    }

    [Fact]
    public void The_reference_photo_is_the_one_that_cannot_be_undone()
    {
        // A record can be deleted and an audit row explained. A biometric baseline overwritten with
        // the wrong person's face is not visible as damage — it just quietly starts failing matches.
        Assert.False(Allowed("POST", "/api/attendance/me/reference-photo"));
    }

    [Theory]
    [InlineData("/api/field-visits/start")]
    [InlineData("/api/field-visits/2b1c0d9e-0000-0000-0000-000000000002/check-in")]
    [InlineData("/api/field-visits/2b1c0d9e-0000-0000-0000-000000000002/check-out")]
    [InlineData("/api/field-visits/2b1c0d9e-0000-0000-0000-000000000002/work-photo")]
    [InlineData("/api/field-visits/2b1c0d9e-0000-0000-0000-000000000002/checklist/3c2d1e0f-0000-0000-0000-000000000003")]
    public void The_worker_half_of_field_visits_is_refused(string path)
    {
        // Same act as a scan, without the poster: a GPS position and a selfie at arrival, filed under
        // whoever the session is borrowing.
        Assert.False(Allowed("POST", path));
    }

    [Theory]
    [InlineData("POST", "/api/field-visits")]
    [InlineData("POST", "/api/field-visits/2b1c0d9e-0000-0000-0000-000000000002/cancel")]
    [InlineData("POST", "/api/field-visits/2b1c0d9e-0000-0000-0000-000000000002/force-checkout")]
    public void The_manager_half_of_field_visits_stays_open(string method, string path)
    {
        // Assigning a visit, cancelling one, closing one somebody left open — support work, done as
        // the admin about somebody else. Nothing of the operator's ends up in it.
        Assert.True(Allowed(method, path));
    }

    [Fact]
    public void An_endpoint_that_does_not_exist_yet_is_refused()
    {
        // The reason this is an allowlist. Whoever adds /api/wellbeing/mood next year does not have to
        // know this file exists for the customer's data to stay clean; they only have to notice that
        // their new screen 403s while impersonating, which is a loud, harmless failure.
        Assert.False(Allowed("POST", "/api/wellbeing/mood"));
        Assert.False(Allowed("POST", "/api/attendance/me/anything-added-later"));
    }

    [Fact]
    public void The_console_prefixes_match_whole_segments_not_string_starts()
    {
        // "/api/administration-of-something" must not pass because it begins with "/api/admin".
        Assert.False(Allowed("POST", "/api/admin-tools/wipe"));
        Assert.True(Allowed("POST", "/api/admin"));
    }
}
