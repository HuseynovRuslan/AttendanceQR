using AttendanceQR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceQR.Infrastructure.Persistence.Configurations;

public class MissedCheckoutRequestConfiguration : IEntityTypeConfiguration<MissedCheckoutRequest>
{
    public void Configure(EntityTypeBuilder<MissedCheckoutRequest> builder)
    {
        builder.ToTable("MissedCheckoutRequests");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Reason)
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(r => r.Status)
            .HasConversion<int>();

        // Requester (required). Restrict — the row is cleaned up via the record cascade below when an
        // employee is force-deleted, so no reviewer/requester block is left dangling.
        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(r => r.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        // Reviewer (optional). SetNull so removing the reviewer keeps the request's history.
        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(r => r.ReviewedByEmployeeId)
            .OnDelete(DeleteBehavior.SetNull);

        // The record the checkout writes to. Cascade so deleting the record (e.g. force-delete of an
        // employee's history) removes its pending request too.
        builder.HasOne<AttendanceRecord>()
            .WithMany()
            .HasForeignKey(r => r.AttendanceRecordId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => new { r.EmployeeId, r.Status });
    }
}
