using AttendanceQR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceQR.Infrastructure.Persistence.Configurations;

public class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.ToTable("Locations");

        builder.HasKey(l => l.Id);

        // (TenantId, Id) is what a branch-scoped role assignment points at — so it can only name a branch of
        // its own company.
        builder.HasAlternateKey(l => new { l.TenantId, l.Id });

        builder.Property(l => l.Name)
            .IsRequired()
            .HasMaxLength(200);

        // TimeOnly maps natively to PostgreSQL `time`, DateOnly to `date` (Npgsql 10).
        builder.Property(l => l.ShiftStart);
        builder.Property(l => l.ShiftEnd);

        // Default true so the column backfills existing rows as active on migration, and new
        // locations are active unless explicitly disabled.
        builder.Property(l => l.IsActive).HasDefaultValue(true);

        // 126 = every day except Sunday — backfills existing rows to the same assumption the app
        // already made everywhere else, rather than defaulting to 0 (no working days at all).
        builder.Property(l => l.WorkDaysMask).HasDefaultValue(126);

        // Optional, and composite on purpose: a branch can only sit in a structure of its OWN company. A
        // null StructureId leaves the key unchecked (MATCH SIMPLE), which is "no structure". Restrict, never
        // cascade or set-null: a structure with branches is not archived, let alone deleted.
        builder.HasOne<Structure>()
            .WithMany()
            .HasForeignKey(l => new { l.TenantId, l.StructureId })
            .HasPrincipalKey(s => new { s.TenantId, s.Id })
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
