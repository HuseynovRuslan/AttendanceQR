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
