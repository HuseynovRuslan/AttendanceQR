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

        // Every report screen (dashboard, tabel, payroll, summary) reads "this tenant, this date
        // range" — at 2000 employees that's ~500k rows/year that would otherwise be filtered by a
        // seq scan over the bare TenantId index.
        builder.HasIndex(s => new { s.TenantId, s.SummaryDate });

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
