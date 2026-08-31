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

        // NOT unique any more, and it cannot be.
        //
        // The number was the re-import's key, so uniqueness was the thing keeping it honest. The
        // import matches on the person now (a renumbered file used to move one person's kit onto the
        // next person's row), which leaves the number as the order to display in — and once it is
        // only an ordering, the table has to be able to hold a repeat. Inserting a line mid-file
        // renumbers everything below it, so mid-import two rows genuinely do want number 16 for a
        // moment; and a line the file no longer contains keeps the number it had, which a new line
        // may legitimately be given. Uniqueness here would turn both into a failed import.
        builder.HasIndex(r => new { r.TenantId, r.RowNo });

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
