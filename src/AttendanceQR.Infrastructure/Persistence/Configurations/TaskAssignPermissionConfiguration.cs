using AttendanceQR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceQR.Infrastructure.Persistence.Configurations;

public class TaskAssignPermissionConfiguration : IEntityTypeConfiguration<TaskAssignPermission>
{
    public void Configure(EntityTypeBuilder<TaskAssignPermission> builder)
    {
        builder.ToTable("TaskAssignPermissions");

        builder.HasKey(p => p.Id);

        // "who can this person send to" and "who can send to this person" are both hot lookups.
        builder.HasIndex(p => p.AssignerEmployeeId);
        builder.HasIndex(p => p.RecipientEmployeeId);

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(p => p.AssignerEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(p => p.RecipientEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
