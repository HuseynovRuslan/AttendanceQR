using AttendanceQR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceQR.Infrastructure.Persistence.Configurations;

public class AuthzShadowEventConfiguration : IEntityTypeConfiguration<AuthzShadowEvent>
{
    public void Configure(EntityTypeBuilder<AuthzShadowEvent> builder)
    {
        builder.ToTable("AuthzShadowEvents");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Kind).IsRequired().HasMaxLength(40);
        builder.Property(e => e.Subject).IsRequired().HasMaxLength(300);
        builder.Property(e => e.TargetKey).IsRequired().HasMaxLength(300);

        // One row per disagreement per day; the writer upserts into it and bumps Count.
        builder.HasIndex(e => new { e.TenantId, e.Day, e.Kind, e.Subject, e.EmployeeId, e.TargetKey }).IsUnique();
    }
}
