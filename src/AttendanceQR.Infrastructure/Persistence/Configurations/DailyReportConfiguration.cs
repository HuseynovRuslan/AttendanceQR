using AttendanceQR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceQR.Infrastructure.Persistence.Configurations;

public class DailyReportRecipientConfiguration : IEntityTypeConfiguration<DailyReportRecipient>
{
    public void Configure(EntityTypeBuilder<DailyReportRecipient> builder)
    {
        builder.ToTable("DailyReportRecipients");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).HasMaxLength(120).IsRequired();
        builder.Property(r => r.LocationIds).HasMaxLength(8000);
        builder.Property(r => r.LinkCodeHash).HasMaxLength(64);
        // The bot's «Start» looks the code up by its hash; the job reads this company's active people.
        builder.HasIndex(r => r.LinkCodeHash);
        builder.HasIndex(r => new { r.TenantId, r.IsActive });
    }
}

public class DailyReportDeliveryConfiguration : IEntityTypeConfiguration<DailyReportDelivery>
{
    public void Configure(EntityTypeBuilder<DailyReportDelivery> builder)
    {
        builder.ToTable("DailyReportDeliveries");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.LastError).HasMaxLength(500);
        builder.Property(d => d.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(d => d.SnapshotJson).IsRequired();

        // One scheduled report per person per day — the guard against sending the leadership the same
        // report twice after a restart or a slow tick. Partial: a manual «send now» is not limited.
        // The job checks first as well: the in-memory test provider does not enforce this.
        builder.HasIndex(d => new { d.TenantId, d.RecipientId, d.ReportDate })
            .IsUnique()
            .HasFilter("\"Kind\" = 0");

        // The retry sweep: what is still waiting and due.
        builder.HasIndex(d => new { d.TenantId, d.Status, d.NextAttemptUtc });

        // Removing a recipient removes the record of what they were sent — there is nothing else to keep
        // it for, and the snapshot holds counts only.
        builder.HasOne<DailyReportRecipient>()
            .WithMany()
            .HasForeignKey(d => d.RecipientId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
