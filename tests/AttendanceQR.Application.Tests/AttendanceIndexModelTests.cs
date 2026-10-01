using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The shape of the AttendanceRecords indexes, asserted on the model rather than trusted to a review.
///
/// Both halves of this matter, and they pull in opposite directions:
///
///   • One person's days need a b-tree that LEADS with the employee. The table had one until
///     2026-09-12, as a side effect of the old UNIQUE (EmployeeId, AttendanceDate) constraint. The
///     split-shift change had to narrow that constraint to a partial one — a day worked in two
///     stretches is two rows — and the lookup index went with it, unnoticed. Production on
///     2026-10-01 was reading ~3,092 rows to return ~4.
///
///   • What must stay impossible is TWO OPEN blocks for one person on one day: a second check-in
///     with no way to say which one a scan closes. That is the partial unique index, and the new
///     index must not quietly re-impose the old whole-table uniqueness on top of it — doing so would
///     refuse the «əlavə qüvvə» crew their second stretch, which is the bug the split-shift work
///     existed to fix.
///
/// So this pins both: the new index exists and is NOT unique, and the old guarantee is untouched.
/// </summary>
public class AttendanceIndexModelTests
{
    private static IEntityType Attendance()
    {
        var tenant = new TenantContext();
        tenant.Resolve(Guid.NewGuid());
        using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"idx-{Guid.NewGuid()}").Options,
            tenant);
        return db.Model.FindEntityType(typeof(AttendanceRecord))!;
    }

    private static IIndex? ByColumns(params string[] props) =>
        Attendance().GetIndexes().FirstOrDefault(i =>
            i.Properties.Select(p => p.Name).SequenceEqual(props));

    [Fact]
    public void The_employee_date_lookup_index_exists_and_leads_with_the_tenant()
    {
        // TenantId first, matching every other index on this table and the global query filter, which
        // puts the tenant into the WHERE clause of every read.
        var ix = ByColumns("TenantId", "EmployeeId", "AttendanceDate");

        Assert.NotNull(ix);
        Assert.Equal("IX_AttendanceRecords_TenantId_EmployeeId_AttendanceDate", ix!.GetDatabaseName());
    }

    [Fact]
    public void The_new_index_is_NOT_unique_and_has_no_filter()
    {
        // Unique here would forbid the second stretch of a split day — the exact bug the split-shift
        // migration was written to remove. A filter would leave closed rows uncovered again, which is
        // the regression this index exists to end.
        var ix = ByColumns("TenantId", "EmployeeId", "AttendanceDate")!;

        Assert.False(ix.IsUnique);
        Assert.Null(ix.GetFilter());
    }

    [Fact]
    public void Two_OPEN_blocks_on_one_day_are_still_forbidden()
    {
        var ix = Attendance().GetIndexes()
            .Single(i => i.GetDatabaseName() == "IX_AttendanceRecords_OneOpenPerDay");

        Assert.True(ix.IsUnique);
        Assert.Equal(["EmployeeId", "AttendanceDate"], ix.Properties.Select(p => p.Name));
        // The filter is what confines the rule to open rows; without it this is the old whole-table
        // constraint again.
        Assert.Equal("\"CheckOutAtUtc\" IS NULL", ix.GetFilter());
    }

    [Fact]
    public void Two_CLOSED_blocks_on_one_day_are_still_allowed()
    {
        // i.e. nothing on this table imposes uniqueness over (EmployeeId, AttendanceDate) for rows
        // that are already closed. A day worked 07:00–11:00 and again 22:00–07:00 is two closed rows.
        var blocking = Attendance().GetIndexes().Where(i =>
            i.IsUnique
            && i.Properties.Select(p => p.Name).ToHashSet().SetEquals(["EmployeeId", "AttendanceDate"])
            && i.GetFilter() is null);

        Assert.Empty(blocking);
    }

    [Fact]
    public void The_indexes_that_were_already_there_are_untouched()
    {
        var names = Attendance().GetIndexes().Select(i => i.GetDatabaseName()).ToHashSet();

        Assert.Contains("IX_AttendanceRecords_OneOpenPerDay", names);
        Assert.Contains("IX_AttendanceRecords_TenantId_AttendanceDate", names);
        Assert.Contains("IX_AttendanceRecords_Open", names);
    }
}
