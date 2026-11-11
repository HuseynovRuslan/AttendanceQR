using AttendanceQR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceQR.Infrastructure.Persistence.Configurations;

public class ChangeRequestEventConfiguration : IEntityTypeConfiguration<ChangeRequestEvent>
{
    public void Configure(EntityTypeBuilder<ChangeRequestEvent> builder)
    {
        builder.ToTable("ChangeRequestEvents");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Kind).HasConversion<int>();
        builder.Property(e => e.Note).HasMaxLength(500);
        builder.Property(e => e.Data).HasColumnType("jsonb");

        // The line carries the company of its request. A line can never hang under another company's request.
        // Cascade only matters to the tenant purge: the code never deletes a request.
        builder.HasOne<ChangeRequest>().WithMany()
            .HasForeignKey(e => new { e.TenantId, e.ChangeRequestId })
            .HasPrincipalKey(r => new { r.TenantId, r.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.TenantId, e.ChangeRequestId, e.AtUtc });

        // A request is decided once. The Version token already makes the second of two simultaneous approvals
        // fail. This is the same rule held by the database itself: one Approved, Rejected, Cancelled or Direct
        // line per request, whatever the code does.
        builder.HasIndex(e => e.ChangeRequestId).IsUnique()
            .HasFilter("\"Kind\" IN (1, 2, 3, 6)")
            .HasDatabaseName("IX_ChangeRequestEvents_OneDecision");
    }
}
