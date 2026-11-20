namespace AttendanceQR.Application.Reporting.DailyReport;

/// <summary>A branch as the report needs it: name, ştat, and the structure it sits under.</summary>
public sealed record DailyReportLocation(Guid Id, string Name, int? Headcount, string? StructureName);

/// <summary>
/// Turns the board's rows into the report's figures. Pure — no database, no clock — so every rule
/// here is a unit test. The rows must come from the same board computation the admin screen uses
/// (<see cref="IReportQueryService.GetBoardForCompanyAsync"/>), and each row is bucketed by
/// <see cref="TodayBuckets"/>, the board's own rule: the leadership and the board read one truth.
/// </summary>
public static class DailyReportBuilder
{
    public static DailyReportData Build(
        string companyName,
        DateOnly date,
        DateTime takenAtLocal,
        IReadOnlyList<DayAttendanceRow> rows,
        IReadOnlyList<DailyReportLocation> locations,
        IReadOnlySet<Guid>? scope,
        IReadOnlyList<string>? preferredOrder = null)
    {
        var inScope = scope is { Count: > 0 } ? scope : null;
        var locById = locations.ToDictionary(l => l.Id);

        var areas = new Dictionary<Guid, DailyReportArea>();
        DailyReportArea AreaFor(Guid id, string fallbackName)
        {
            if (areas.TryGetValue(id, out var a)) return a;
            locById.TryGetValue(id, out var loc);
            a = new DailyReportArea
            {
                LocationId = id,
                Name = loc?.Name ?? fallbackName,
                Headcount = loc?.Headcount is > 0 ? loc.Headcount : null,
                StructureName = loc?.StructureName,
            };
            areas[id] = a;
            return a;
        }

        foreach (var r in rows)
        {
            if (inScope != null && !inScope.Contains(r.LocationId)) continue;
            AreaFor(r.LocationId, r.LocationName).Counts.Add(TodayBuckets.BucketOf(r));
        }
        // A branch with a ştat but nobody in it yet is exactly the vacancy the leadership needs to see.
        foreach (var loc in locations)
        {
            if (inScope != null && !inScope.Contains(loc.Id)) continue;
            if (loc.Headcount is > 0) AreaFor(loc.Id, loc.Name);
        }

        var order = preferredOrder ?? [];
        var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < order.Count; i++) rank.TryAdd(order[i].Trim(), i);

        var ordered = areas.Values
            .OrderBy(a => rank.TryGetValue(a.Name.Trim(), out var i) ? i : int.MaxValue)
            .ThenByDescending(a => a.Counts.Employees)
            .ThenBy(a => a.Name, StringComparer.Create(new System.Globalization.CultureInfo("az-Latn-AZ"), false))
            .ToList();

        var totals = new DailyReportCounts();
        foreach (var a in ordered) totals.AddAll(a.Counts);

        foreach (var a in ordered.Append(new DailyReportArea { Name = "CƏMİ", Counts = totals }))
        {
            if (!a.Counts.IsConsistent)
                throw new InvalidOperationException(
                    $"Daily report counts do not add up for '{a.Name}': {a.Counts.SumOfParts} ≠ {a.Counts.Employees}");
        }

        var withHeadcount = ordered.Where(a => a.Headcount.HasValue).ToList();
        var structures = ordered.Where(a => !string.IsNullOrWhiteSpace(a.StructureName))
            .GroupBy(a => a.StructureName!)
            .Select(g => new DailyReportStructure
            {
                Name = g.Key,
                Areas = g.Count(),
                Employees = g.Sum(a => a.Counts.Employees),
                Present = g.Sum(a => a.Counts.Present),
            })
            .OrderByDescending(s => s.Employees)
            .ToList();

        return new DailyReportData
        {
            CompanyName = companyName,
            Date = date,
            TakenAtLocal = takenAtLocal,
            ScopeNote = inScope == null ? null : $"{ordered.Count} ərazi üzrə",
            Areas = ordered,
            Totals = totals,
            HeadcountTotal = withHeadcount.Count == 0 ? null : withHeadcount.Sum(a => a.Headcount!.Value),
            Vacancies = withHeadcount.Sum(a => a.Vacancies),
            Structures = structures,
        };
    }
}
