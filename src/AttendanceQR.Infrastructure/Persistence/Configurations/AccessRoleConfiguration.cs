using AttendanceQR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceQR.Infrastructure.Persistence.Configurations;

public class AccessRoleConfiguration : IEntityTypeConfiguration<AccessRole>
{
    public void Configure(EntityTypeBuilder<AccessRole> builder)
    {
        builder.ToTable("AccessRoles");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).IsRequired().HasMaxLength(120);
        builder.Property(r => r.Description).HasMaxLength(500);
        builder.Property(r => r.TemplateKey).HasMaxLength(60);

        // What an assignment points at, so a role of one company can never be assigned in another.
        builder.HasAlternateKey(r => new { r.TenantId, r.Id });

        // One role per template per company — the seeder relies on finding exactly one.
        builder.HasIndex(r => new { r.TenantId, r.TemplateKey })
            .IsUnique()
            .HasFilter("\"TemplateKey\" IS NOT NULL");
        builder.HasIndex(r => new { r.TenantId, r.Name })
            .IsUnique()
            .HasFilter("\"ArchivedAtUtc\" IS NULL");
    }
}
