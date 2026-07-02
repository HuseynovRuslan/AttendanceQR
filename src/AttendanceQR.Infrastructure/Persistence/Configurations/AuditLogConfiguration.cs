using AttendanceQR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceQR.Infrastructure.Persistence.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.EventType)
            .HasConversion<int>();

        builder.Property(a => a.Reason)
            .HasMaxLength(1000);

        builder.Property(a => a.IpAddress)
            .HasMaxLength(64);

        // Optional employee link; keep the audit entry (set null) if the employee is removed.
        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(a => a.EmployeeId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
