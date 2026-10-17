using AttendanceQR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceQR.Infrastructure.Persistence.Configurations;

public class StructureConfiguration : IEntityTypeConfiguration<Structure>
{
    public void Configure(EntityTypeBuilder<Structure> builder)
    {
        builder.ToTable("Structures");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).IsRequired().HasMaxLength(120);

        // (TenantId, Id) is what a branch points at, so the database itself refuses a branch of one company
        // inside another company's structure — not only the query filter in the code that set it.
        builder.HasAlternateKey(s => new { s.TenantId, s.Id });

        // One live structure per name in a company; an archived one frees its name.
        builder.HasIndex(s => new { s.TenantId, s.Name })
            .IsUnique()
            .HasFilter("\"ArchivedAtUtc\" IS NULL");
    }
}
