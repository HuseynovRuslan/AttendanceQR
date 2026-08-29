using AttendanceQR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceQR.Infrastructure.Persistence.Configurations;

public class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> builder)
    {
        builder.ToTable("Assets");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.InventoryNumber)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(a => a.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(a => a.Brand).HasMaxLength(100);
        builder.Property(a => a.Model).HasMaxLength(100);
        builder.Property(a => a.SerialNumber).HasMaxLength(100);
        builder.Property(a => a.Notes).HasMaxLength(1000);

        builder.Property(a => a.Type).HasConversion<int>();
        builder.Property(a => a.Status).HasConversion<int>();

        builder.Property(a => a.PurchasePrice).HasPrecision(12, 2);

        // "Who has inventory number 000431?" is the question this table exists to answer, so the
        // number has to identify exactly one device within the company.
        builder.HasIndex(a => new { a.TenantId, a.InventoryNumber }).IsUnique();

        // The other lookup that matters: everything one employee is responsible for.
        builder.HasIndex(a => a.AssignedEmployeeId);

        // Restrict, not SetNull: an employee who still holds a laptop must hand it back before the
        // account is deleted. SetNull would quietly detach the row and lose the only record of where
        // the device went.
        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(a => a.AssignedEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Location>()
            .WithMany()
            .HasForeignKey(a => a.LocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
