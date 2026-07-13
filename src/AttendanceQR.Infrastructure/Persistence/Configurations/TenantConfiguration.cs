using AttendanceQR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceQR.Infrastructure.Persistence.Configurations;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> b)
    {
        b.ToTable("Tenants");
        b.HasKey(t => t.Id);

        b.Property(t => t.Name).IsRequired().HasMaxLength(200);
        b.Property(t => t.Slug).IsRequired().HasMaxLength(60);
        b.HasIndex(t => t.Slug).IsUnique();
        b.Property(t => t.DisplayName).IsRequired().HasMaxLength(200);
        b.Property(t => t.LogoKey).HasMaxLength(512);
        b.Property(t => t.Color).HasMaxLength(20);
    }
}
