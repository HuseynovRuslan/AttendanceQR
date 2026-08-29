using AttendanceQR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceQR.Infrastructure.Persistence.Configurations;

public class EquipmentRecordConfiguration : IEntityTypeConfiguration<EquipmentRecord>
{
    public void Configure(EntityTypeBuilder<EquipmentRecord> builder)
    {
        builder.ToTable("EquipmentRecords");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.FullName).IsRequired().HasMaxLength(200);
        builder.Property(r => r.Position).HasMaxLength(200);
        builder.Property(r => r.Area).HasMaxLength(200);

        // The four equipment columns are prose copied from the register, several lines each. 2000 is
        // roughly four times the longest line in the current file — room to grow without being a
        // limit anyone meets by typing.
        builder.Property(r => r.Equipment).HasMaxLength(2000);
        builder.Property(r => r.SystemUnit).HasMaxLength(2000);
        builder.Property(r => r.Monitor).HasMaxLength(2000);
        builder.Property(r => r.OtherEquipment).HasMaxLength(2000);

        // A re-import matches on this, so two lines cannot share a number.
        builder.HasIndex(r => new { r.TenantId, r.RowNo }).IsUnique();

        builder.HasIndex(r => r.EmployeeId);

        // SetNull, not Restrict: the line names the person in text as well, so deleting the staff
        // record loses the link but not the fact that someone holds the kit — which is exactly the row
        // an admin needs to see after somebody leaves.
        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(r => r.EmployeeId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
