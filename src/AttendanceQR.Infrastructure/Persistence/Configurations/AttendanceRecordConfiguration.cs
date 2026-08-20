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

        // One record per employee per day.
        builder.HasIndex(a => new { a.EmployeeId, a.AttendanceDate })
            .IsUnique();

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
