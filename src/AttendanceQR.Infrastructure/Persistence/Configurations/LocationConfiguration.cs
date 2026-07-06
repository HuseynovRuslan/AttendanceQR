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
    }
}
