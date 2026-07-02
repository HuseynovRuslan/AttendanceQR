using AttendanceQR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceQR.Infrastructure.Persistence.Configurations;

public class DailySummaryConfiguration : IEntityTypeConfiguration<DailySummary>
{
    public void Configure(EntityTypeBuilder<DailySummary> builder)
    {
        builder.ToTable("DailySummaries");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Status)
            .HasConversion<int>();

        // One summary per employee per day.
        builder.HasIndex(s => new { s.EmployeeId, s.SummaryDate })
            .IsUnique();

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(s => s.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Location>()
            .WithMany()
            .HasForeignKey(s => s.LocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
