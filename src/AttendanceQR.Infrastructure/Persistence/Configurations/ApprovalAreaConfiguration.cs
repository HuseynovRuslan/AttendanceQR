using AttendanceQR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceQR.Infrastructure.Persistence.Configurations;

public class ApprovalAreaConfiguration : IEntityTypeConfiguration<ApprovalArea>
{
    public void Configure(EntityTypeBuilder<ApprovalArea> builder)
    {
        // Structure (1) or Branch (2), never the whole company (0). There is no company-wide switch.
        builder.ToTable("ApprovalAreas", t => t.HasCheckConstraint(
            "CK_ApprovalAreas_Scope",
            "(\"Scope\" = 1 AND \"StructureId\" IS NOT NULL AND \"LocationId\" IS NULL)"
            + " OR (\"Scope\" = 2 AND \"LocationId\" IS NOT NULL AND \"StructureId\" IS NULL)"));
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Scope).HasConversion<int>();

        builder.HasOne<Structure>().WithMany()
            .HasForeignKey(a => new { a.TenantId, a.StructureId })
            .HasPrincipalKey(s => new { s.TenantId, s.Id })
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Location>().WithMany()
            .HasForeignKey(a => new { a.TenantId, a.LocationId })
            .HasPrincipalKey(l => new { l.TenantId, l.Id })
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        // One live switch per area. Pressing «Aktivləşdir» twice, or twice at once, does not make two.
        builder.HasIndex(a => new { a.TenantId, a.Scope, a.StructureId, a.LocationId })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasFilter("\"DeactivatedAtUtc\" IS NULL");
    }
}
