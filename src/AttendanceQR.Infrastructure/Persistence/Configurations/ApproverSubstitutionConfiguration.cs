using AttendanceQR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceQR.Infrastructure.Persistence.Configurations;

public class ApproverSubstitutionConfiguration : IEntityTypeConfiguration<ApproverSubstitution>
{
    public void Configure(EntityTypeBuilder<ApproverSubstitution> builder)
    {
        // A stand-in always ends. An open-ended one is the company-wide approver the owner said no to.
        builder.ToTable("ApproverSubstitutions", t => t.HasCheckConstraint(
            "CK_ApproverSubstitutions_Period", "\"EndsAtUtc\" > \"StartsAtUtc\""));
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Note).HasMaxLength(500);

        builder.HasOne<Structure>().WithMany()
            .HasForeignKey(s => new { s.TenantId, s.StructureId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        // A stand-in means nothing without the person. Deleting the account ends it with them.
        builder.HasOne<Employee>().WithMany()
            .HasForeignKey(s => s.SubstituteEmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        // Read on every decision: the live stand-ins of one structure.
        builder.HasIndex(s => new { s.TenantId, s.StructureId, s.EndsAtUtc }).HasFilter("\"RevokedAtUtc\" IS NULL");
    }
}
