using AttendanceQR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceQR.Infrastructure.Persistence.Configurations;

public class PresenceCheckConfiguration : IEntityTypeConfiguration<PresenceCheck>
{
    public void Configure(EntityTypeBuilder<PresenceCheck> builder)
    {
        builder.ToTable("PresenceChecks");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Result).HasConversion<int>();
        builder.Property(c => c.Note).HasMaxLength(40);

        // The worker's two reads: «due and not sent» and «sent and still waiting». Both are this tenant's
        // pending rows by time, and the table only ever holds a handful of people's days.
        builder.HasIndex(c => new { c.TenantId, c.Result, c.DueAtUtc });

        // The profile's history and the daily cap: one person's checks, newest first.
        builder.HasIndex(c => new { c.TenantId, c.EmployeeId, c.CheckDate });

        // A day is planned once. Two sweeps racing each other — or a sweep after a restart — get one plan and a
        // 23505, never four checks on the same day. The code checks first as well: the in-memory test provider
        // does not enforce this.
        builder.HasIndex(c => new { c.AttendanceRecordId, c.Slot }).IsUnique();

        // Cascade both ways: a check means nothing without the person or the day it was asked on, and must not
        // stand in the way of deleting either (the employee delete removes their days with ExecuteDelete,
        // which only the database's cascade can follow).
        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(c => c.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<AttendanceRecord>()
            .WithMany()
            .HasForeignKey(c => c.AttendanceRecordId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
