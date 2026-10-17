using AttendanceQR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceQR.Infrastructure.Persistence.Configurations;

public class RoleAssignmentConfiguration : IEntityTypeConfiguration<RoleAssignment>
{
    public void Configure(EntityTypeBuilder<RoleAssignment> builder)
    {
        builder.ToTable("RoleAssignments", t => t.HasCheckConstraint(
            "CK_RoleAssignments_Scope",
            "(\"Scope\" = 0 AND \"StructureId\" IS NULL AND \"LocationId\" IS NULL)"
            + " OR (\"Scope\" = 1 AND \"StructureId\" IS NOT NULL AND \"LocationId\" IS NULL)"
            + " OR (\"Scope\" = 2 AND \"LocationId\" IS NOT NULL AND \"StructureId\" IS NULL)"));
        builder.HasKey(a => a.Id);

        // Every reference carries the company: a role, a structure or a branch of another company cannot be
        // named here, by the code or by a hand-written UPDATE.
        builder.HasOne<AccessRole>().WithMany()
            .HasForeignKey(a => new { a.TenantId, a.AccessRoleId })
            .HasPrincipalKey(r => new { r.TenantId, r.Id })
            .OnDelete(DeleteBehavior.Restrict);
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
        builder.HasOne<Employee>().WithMany()
            .HasForeignKey(a => a.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        // Read on every panel request: one person's live assignments.
        builder.HasIndex(a => new { a.TenantId, a.EmployeeId }).HasFilter("\"RevokedAtUtc\" IS NULL");

        // The same role over the same area is held once.
        builder.HasIndex(a => new { a.EmployeeId, a.AccessRoleId, a.Scope, a.StructureId, a.LocationId })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasFilter("\"RevokedAtUtc\" IS NULL");
    }
}
