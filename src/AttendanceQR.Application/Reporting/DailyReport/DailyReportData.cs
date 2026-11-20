namespace AttendanceQR.Application.Reporting.DailyReport;

/// <summary>
/// The daily management report's figures — what is frozen into a delivery's snapshot and what both the
/// PDF and the HTML page are drawn from. Counts only: no names, no phone numbers, nobody's day in detail.
/// </summary>
public sealed class DailyReportData
{
    public string CompanyName { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    /// <summary>Local wall-clock moment the figures were taken («18:00 vəziyyəti»).</summary>
    public DateTime TakenAtLocal { get; set; }
    /// <summary>Empty = the whole company; otherwise a short note that this is a partial view.</summary>
    public string? ScopeNote { get; set; }
    public List<DailyReportArea> Areas { get; set; } = [];
    public DailyReportCounts Totals { get; set; } = new();
    /// <summary>Sum of ştat over the branches that have one; null when no branch in scope has one set.</summary>
    public int? HeadcountTotal { get; set; }
    /// <summary>Σ max(0, ştat − işçi sayı) over branches with a ştat.</summary>
    public int Vacancies { get; set; }
    public List<DailyReportStructure> Structures { get; set; } = [];
}

public sealed class DailyReportArea
{
    public Guid? LocationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? StructureName { get; set; }
    public int? Headcount { get; set; }
    public DailyReportCounts Counts { get; set; } = new();
    public int Vacancies => Headcount is int h ? Math.Max(0, h - Counts.Employees) : 0;
}

public sealed class DailyReportStructure
{
    public string Name { get; set; } = string.Empty;
    public int Areas { get; set; }
    public int Employees { get; set; }
    public int Present { get; set; }
}

/// <summary>
/// One area's (or the company's) people by where they are today. The columns partition the employees:
/// every person is in exactly one of them, so <see cref="Employees"/> always equals their sum
/// (<see cref="IsConsistent"/>). Built from the board's own buckets — see TodayBuckets.
/// </summary>
public sealed class DailyReportCounts
{
    public int Employees { get; set; }
    /// <summary>İşdə — came to work today: finished, still at work, or out on a site (present + incomplete).</summary>
    public int Present { get; set; }
    /// <summary>İstirahət — a granted rest day plus the roster's own day off (and holidays).</summary>
    public int Rest { get; set; }
    /// <summary>Məzuniyyət — annual leave only.</summary>
    public int Vacation { get; set; }
    /// <summary>İşə gəlməyib — due today, shift has started, no scan.</summary>
    public int Absent { get; set; }
    public int Permission { get; set; }
    public int Sick { get; set; }
    /// <summary>Ezamiyyət — away working.</summary>
    public int Trip { get; set; }
    public int Unpaid { get; set; }
    /// <summary>Sərbəst qrafik, giriş yoxdur — never counted as absent (nobody was due at a given hour).</summary>
    public int FlexNoScan { get; set; }
    /// <summary>Aktivləşdirməyib — activated account, has never scanned yet.</summary>
    public int Onboarding { get; set; }
    /// <summary>Növbəsi hələ başlamayıb.</summary>
    public int Pending { get; set; }

    public int SumOfParts => Present + Rest + Vacation + Absent + Permission + Sick + Trip + Unpaid + FlexNoScan + Onboarding + Pending;
    public bool IsConsistent => SumOfParts == Employees;

    public void Add(string bucket)
    {
        Employees++;
        switch (bucket)
        {
            case TodayBuckets.Present:
            case TodayBuckets.Incomplete: Present++; break;
            case TodayBuckets.Rest:
            case TodayBuckets.DayOff: Rest++; break;
            case TodayBuckets.OnLeave: Vacation++; break;
            case TodayBuckets.Absent: Absent++; break;
            case TodayBuckets.Permission: Permission++; break;
            case TodayBuckets.Sick: Sick++; break;
            case TodayBuckets.Trip: Trip++; break;
            case TodayBuckets.Unpaid: Unpaid++; break;
            case TodayBuckets.FlexNoScan: FlexNoScan++; break;
            case TodayBuckets.Onboarding: Onboarding++; break;
            case TodayBuckets.Pending: Pending++; break;
            default: throw new InvalidOperationException($"Unknown bucket '{bucket}'");
        }
    }

    public void AddAll(DailyReportCounts o)
    {
        Employees += o.Employees; Present += o.Present; Rest += o.Rest; Vacation += o.Vacation; Absent += o.Absent;
        Permission += o.Permission; Sick += o.Sick; Trip += o.Trip; Unpaid += o.Unpaid; FlexNoScan += o.FlexNoScan;
        Onboarding += o.Onboarding; Pending += o.Pending;
    }
}
