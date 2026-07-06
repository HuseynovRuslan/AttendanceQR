using AttendanceQR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceQR.Infrastructure.Persistence.Configurations;

public class LeaveRecordConfiguration : IEntityTypeConfiguration<LeaveRecord>
{
    public void Configure(EntityTypeBuilder<LeaveRecord> builder)
    {
        builder.ToTable("LeaveRecords");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Type).HasConversion<int>();

        builder.Property(l => l.Note).HasMaxLength(500);

        builder.HasIndex(l => new { l.EmployeeId, l.FromDate, l.ToDate });

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(l => l.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
