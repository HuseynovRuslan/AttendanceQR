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
        //
        // The sentinel is what makes 0 a value EF sends. A column with a database default is left out of the
        // INSERT whenever the property holds its sentinel, and the sentinel is the CLR default unless told
        // otherwise — 0, which for this column is a real answer an admin can give («no day is a working day
        // here; the people's own shifts decide»). So a branch created with every day unticked was stored as
        // Monday–Saturday by Postgres, and its staff without a shift could turn Qayıb on days the admin had
        // declared off. -1 is no mask anyone can save (TryValidate takes 0..127), so every value is now
        // written as given. The column default stays: it is the backfill, and changing it is not needed.
        // The in-memory tests could not see any of it — that provider ignores database defaults.
        builder.Property(l => l.WorkDaysMask).HasDefaultValue(126).HasSentinel(-1);

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
