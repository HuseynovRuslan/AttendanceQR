using AttendanceQR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceQR.Infrastructure.Persistence.Configurations;

public class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.ToTable("Tasks");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Status).HasConversion<int>();

        builder.Property(t => t.Title).HasMaxLength(200);
        builder.Property(t => t.Description).HasMaxLength(2000);

        // The two hot lookups: "tasks assigned to me" and "tasks I assigned" — both filtered by status.
        builder.HasIndex(t => new { t.AssignedToEmployeeId, t.Status });
        builder.HasIndex(t => new { t.AssignedByEmployeeId, t.Status });

        // Restrict, not Cascade: an employee row with tasks still pointing at it can't be hard-deleted
        // out from under this table (employees are deactivated, not deleted, in this app anyway).
        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(t => t.AssignedToEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(t => t.AssignedByEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
