using AttendanceQR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceQR.Infrastructure.Persistence.Configurations;

public class AttendanceRecordConfiguration : IEntityTypeConfiguration<AttendanceRecord>
{
    public void Configure(EntityTypeBuilder<AttendanceRecord> builder)
    {
        builder.ToTable("AttendanceRecords");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Status)
            .HasConversion<int>();

        // Photo-audit object key (points into MinIO). Keys are short; give headroom.
        builder.Property(a => a.CheckInPhotoKey)
            .HasMaxLength(256);

        // Face-audit status stored as int (like Status).
        builder.Property(a => a.FaceMatchStatus)
            .HasConversion<int>();

        // ONE OPEN record per employee per day — not one record per day.
        //
        // The old rule was "one record per employee per day", and it was never the guarantee that
        // mattered; it was a side effect of assuming everybody's day is a single stretch. A crew that
        // washes an area from 07:00 to 11:00, goes home and comes back at 22:00 works one day in two
        // stretches, and under the old index the 11:00 check-out closed the day and the 22:00 arrival
        // was refused — nine hours of night work recorded nowhere at all.
        //
        // What has to stay impossible is TWO OPEN blocks: a person checked in twice with no way to say
        // which one a scan closes, which is how a day ends up spanning "in at 04:46, out at 22:01".
        // That is exactly what this filtered index still forbids, on every employee, split shift or
        // not. A second stretch may only be opened once the first is closed, and only by a shift that
        // declares a second window — see SplitShiftRules.
        builder.HasIndex(a => new { a.EmployeeId, a.AttendanceDate }, "IX_AttendanceRecords_OneOpenPerDay")
            .IsUnique()
            .HasFilter("\"CheckOutAtUtc\" IS NULL");

        // Date-leading tenant index: the today board, the admin bell counts, the 5-minute reminder
        // sweep, the nightly summary and announcement targeting all read "this tenant, this date".
        // The bare TenantId index is worthless once one tenant owns most of the table — at 2000
        // employees this is the difference between a point lookup and a 600k-row/year seq scan.
        // Both indexes cover the same columns, so both need explicit names — unnamed, EF treats the
        // second declaration as the first and silently emits only one.
        builder.HasIndex(a => new { a.TenantId, a.AttendanceDate }, "IX_AttendanceRecords_TenantId_AttendanceDate");

        // Partial index for the open-records question ("checked in, never out, before today") that
        // the sidebar badge and bell recount on every admin poll. Open rows are a tiny sliver of the
        // table, so the filtered index stays a few hundred rows no matter how the history grows.
        builder.HasIndex(a => new { a.TenantId, a.AttendanceDate }, "IX_AttendanceRecords_Open")
            .HasFilter("\"CheckInAtUtc\" IS NOT NULL AND \"CheckOutAtUtc\" IS NULL");

        // ONE PERSON's days — the lookup every screen about an individual makes, and the one the
        // table had no index for between 2026-09-12 and this migration.
        //
        // It went missing as a side effect rather than a decision. The split-shift change narrowed
        // the old UNIQUE (EmployeeId, AttendanceDate) constraint into the partial OneOpenPerDay above
        // (it had to: a day worked in two stretches is two rows), and with it went the only b-tree
        // that led with EmployeeId. Nothing replaced it, because the constraint had been doing the
        // lookup's job for free.
        //
        // What that costs, measured on production 2026-10-01: a thirty-row attendance history was
        // planned as a bitmap scan of IX_..._TenantId_AttendanceDate with EmployeeId applied as a
        // heap FILTER — ~3,092 rows read to return ~4. Across the table, that index had read
        // 1.33 billion entries to yield 95 million: a 7% hit rate. The partial index sat unused for
        // these queries because it only covers rows that are still open.
        //
        // TenantId leads, matching every other index here and the global query filter, which puts
        // the tenant into the WHERE clause of every single read. Non-unique on purpose: a day may
        // legitimately hold more than one record, and the uniqueness that still matters — never two
        // OPEN blocks — is the partial index above, untouched.
        builder.HasIndex(a => new { a.TenantId, a.EmployeeId, a.AttendanceDate },
            "IX_AttendanceRecords_TenantId_EmployeeId_AttendanceDate");

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(a => a.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Location>()
            .WithMany()
            .HasForeignKey(a => a.LocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
