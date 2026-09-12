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
