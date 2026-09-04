using AttendanceQR.Api;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The gate that lets the customer's group head READ every company and change nothing in any of them.
///
/// It is the only thing standing between a «baxış rejimi» session and 113 mutating endpoints, so the
/// rule is deliberately method-level rather than per-endpoint: an endpoint written next week is
/// refused without anybody remembering to refuse it. These tests pin both halves — that safe methods
/// pass, and that the ONE POST exception has not quietly grown.
/// </summary>
public class ViewOnlyBoundaryTests
{
    private static bool Allowed(string method, string path) =>
        ViewOnlyBoundary.IsAllowed(method, new PathString(path));

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public void Reading_is_the_whole_point(string method)
    {
        Assert.True(Allowed(method, "/api/reports/today"));
        Assert.True(Allowed(method, "/api/admin/employees"));
    }

    [Theory]
    [InlineData("POST", "/api/admin/employees")]
    [InlineData("PUT", "/api/admin/employees/11111111-1111-1111-1111-111111111111")]
    [InlineData("DELETE", "/api/admin/attendance/11111111-1111-1111-1111-111111111111")]
    [InlineData("PATCH", "/api/admin/employees")]
    [InlineData("POST", "/api/attendance/scan")]
    [InlineData("POST", "/api/admin/employees/bulk-schedule")]
    [InlineData("POST", "/api/leaves")]
    public void Every_mutating_request_is_refused(string method, string path)
    {
        Assert.False(Allowed(method, path));
    }

    [Fact]
    public void The_export_is_the_single_POST_that_passes()
    {
        // It formats rows the client already holds into a spreadsheet and writes nothing. A person
        // whose entire job here is reading reports must be able to take one away with them.
        Assert.True(Allowed("POST", "/api/reports/export-day"));
    }

    [Fact]
    public void A_report_that_rewrites_the_day_is_a_write_however_it_is_named()
    {
        // /api/admin/reports/generate calls GenerateForDateAsync, which REBUILDS that day's summaries
        // for the whole company. It reads like a report and behaves like a migration.
        Assert.False(Allowed("POST", "/api/admin/reports/generate"));
    }

    [Fact]
    public void Parsing_an_upload_is_step_one_of_an_import_and_is_refused()
    {
        // parse-xlsx touches no table, but a viewer has no business beginning an import at all —
        // and the allowlist is the one place where "harmless today" becomes "allowed forever".
        Assert.False(Allowed("POST", "/api/admin/employees/parse-xlsx"));
    }

    [Fact]
    public void The_allowlist_is_matched_by_segment_not_by_prefix()
    {
        // "/api/reports/export-day-and-delete-everything" must not slip through on a string prefix.
        Assert.False(Allowed("POST", "/api/reports/export-daydream"));
        // …while the real path still passes with a trailing segment or query-shaped suffix.
        Assert.True(Allowed("POST", "/api/reports/export-day"));
        Assert.True(Allowed("POST", "/api/reports/export-day/xlsx"));
    }

    [Fact]
    public void Case_does_not_matter_in_the_path()
    {
        // Routing is case-insensitive; the gate must not be a way round itself.
        Assert.True(Allowed("POST", "/API/Reports/Export-Day"));
    }
}
