using AttendanceQR.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// The roster BY PAPER — everyone one employer's documents claim, wherever they actually work.
///
/// A tenant panel can never answer this. Çingiz Hümbətov works at Green Garden and is on Bakı Abadlıq
/// Xidməti's books; his row lives in Green Garden's data, so Bakı Abadlıq's own panel cannot see him
/// and never will — one company's panel reading another's rows is the wall this product is built on.
/// Inside a panel a «sənəd üzrə» switch could therefore only ever SUBTRACT rows, never add them,
/// producing a file that looks complete and is not. That is the worst kind of report: the reader has
/// no way to know what is missing.
///
/// So it lives here instead, in the one place allowed to read across companies — gated on the
/// super-admin allowlist, not on a role a company's own admin could grant themselves. This is the
/// file the accountant actually asks for when one legal entity pays people standing at three
/// different companies' sites.
///
/// THE RULE, once: a person's paper employer is <c>PaperEmployer ?? their own company's name</c>.
/// Null means "the paperwork agrees with where they are", which is true of all but a handful. Every
/// figure on this screen and in its workbook comes from that one expression — there is no second
/// definition to drift.
/// </summary>
public partial class SuperAdminController
{
    /// <summary>One person as the paper sees them, next to where they actually are.</summary>
    private sealed record PaperPerson(
        Guid Id,
        string FullName,
        string? Position,
        string PaperEmployer,
        string? PaperSite,
        string ActualCompany,
        string ActualSite,
        string? PhoneNumber,
        bool IsActive,
        bool Elsewhere);

    /// <summary>
    /// Everyone in the group, resolved to their paper employer. Read once and filtered in memory:
    /// the whole group is ~660 rows, and doing it in one pass keeps the JSON and the workbook
    /// provably identical.
    /// </summary>
    private async Task<List<PaperPerson>> PaperRosterAsync(CancellationToken ct)
    {
        var tenantNames = await _db.Tenants.IgnoreQueryFilters()
            .Select(t => new { t.Id, t.Name, t.DisplayName })
            .ToDictionaryAsync(
                t => t.Id,
                t => string.IsNullOrWhiteSpace(t.DisplayName) ? t.Name : t.DisplayName,
                ct);

        var locationNames = await _db.Locations.IgnoreQueryFilters()
            .Select(l => new { l.Id, l.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        var people = await _db.Employees.IgnoreQueryFilters()
            .Select(e => new
            {
                e.Id, e.TenantId, e.FullName, e.Position, e.LocationId,
                e.PhoneNumber, e.IsActive, e.PaperEmployer, e.PaperSite, e.Email,
            })
            .ToListAsync(ct);

        return people
            // The operator's own hidden accounts are not staff and must not appear on a roster the
            // customer reads — the same list every tenant report already honours.
            .Where(e => !_appOptions.HiddenEmailList().Contains(e.Email ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            .Select(e =>
            {
                var company = tenantNames.GetValueOrDefault(e.TenantId, "—");
                var paper = string.IsNullOrWhiteSpace(e.PaperEmployer) ? company : e.PaperEmployer!;
                return new PaperPerson(
                    e.Id, e.FullName, e.Position,
                    paper,
                    string.IsNullOrWhiteSpace(e.PaperSite) ? null : e.PaperSite,
                    company,
                    locationNames.GetValueOrDefault(e.LocationId, "—"),
                    e.PhoneNumber,
                    e.IsActive,
                    // "Borrowed": the documents name a different company from the one running their
                    // account. The only rows where the two columns actually disagree.
                    !string.Equals(paper, company, StringComparison.OrdinalIgnoreCase));
            })
            .ToList();
    }

    // GET /api/super/hq/paper-roster?employer=&onlyElsewhere=&includeInactive=
    //
    // Without `employer` it returns the whole group so the console can list who exists and how many
    // each employer carries; with one, just that employer's people.
    [HttpGet("hq/paper-roster")]
    public async Task<IActionResult> PaperRoster(
        string? employer = null, bool onlyElsewhere = false, bool includeInactive = false)
    {
        if (!IsSuperAdmin)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "NotSuperAdmin" });

        var all = await PaperRosterAsync(HttpContext.RequestAborted);
        var rows = Filter(all, employer, onlyElsewhere, includeInactive);

        return Ok(new
        {
            // Every employer the group knows of, with its headcount BY PAPER — the picker's options
            // and, incidentally, the answer to "how many people does this entity actually employ".
            employers = all
                .Where(p => p.IsActive)
                .GroupBy(p => p.PaperEmployer)
                .Select(g => new
                {
                    name = g.Key,
                    total = g.Count(),
                    elsewhere = g.Count(p => p.Elsewhere),
                })
                .OrderByDescending(x => x.total)
                .ToList(),
            employer,
            rows = rows.Select(p => new
            {
                id = p.Id,
                fullName = p.FullName,
                position = p.Position,
                paperEmployer = p.PaperEmployer,
                paperSite = p.PaperSite,
                actualCompany = p.ActualCompany,
                actualSite = p.ActualSite,
                phoneNumber = p.PhoneNumber,
                isActive = p.IsActive,
                elsewhere = p.Elsewhere,
            }),
        });
    }

    // GET /api/super/hq/paper-roster/export — the same rows as a formatted .xlsx.
    [HttpGet("hq/paper-roster/export")]
    public async Task<IActionResult> PaperRosterExport(
        string? employer = null, bool onlyElsewhere = false, bool includeInactive = false)
    {
        if (!IsSuperAdmin)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "NotSuperAdmin" });

        var rows = Filter(await PaperRosterAsync(HttpContext.RequestAborted), employer, onlyElsewhere, includeInactive);
        var bytes = PaperRosterSheet.Build(
            rows.Select(p => new PaperRosterSheet.Row(
                p.FullName, p.Position, p.PaperEmployer, p.PaperSite,
                p.ActualCompany, p.ActualSite, p.PhoneNumber, p.IsActive, p.Elsewhere)).ToList(),
            employer, onlyElsewhere);

        var stamp = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var name = string.IsNullOrWhiteSpace(employer) ? "qrup" : Slugify(employer);
        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"sened-uzre_{name}_{stamp}.xlsx");
    }

    private static List<PaperPerson> Filter(
        List<PaperPerson> all, string? employer, bool onlyElsewhere, bool includeInactive)
    {
        IEnumerable<PaperPerson> q = all;
        if (!includeInactive) q = q.Where(p => p.IsActive);
        if (!string.IsNullOrWhiteSpace(employer))
            q = q.Where(p => string.Equals(p.PaperEmployer, employer, StringComparison.OrdinalIgnoreCase));
        if (onlyElsewhere) q = q.Where(p => p.Elsewhere);
        return q.OrderBy(p => p.ActualCompany).ThenBy(p => p.ActualSite).ThenBy(p => p.FullName).ToList();
    }

    /// <summary>File-name safe: the employer is a company name with Azerbaijani letters and spaces.</summary>
    private static string Slugify(string s)
    {
        var map = new Dictionary<char, char>
        {
            ['ə'] = 'e', ['Ə'] = 'e', ['ı'] = 'i', ['İ'] = 'i', ['ö'] = 'o', ['Ö'] = 'o',
            ['ü'] = 'u', ['Ü'] = 'u', ['ç'] = 'c', ['Ç'] = 'c', ['ş'] = 's', ['Ş'] = 's',
            ['ğ'] = 'g', ['Ğ'] = 'g',
        };
        var chars = s.Select(c => map.TryGetValue(c, out var m) ? m : char.ToLowerInvariant(c))
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        return new string(chars).Trim('-');
    }

}
