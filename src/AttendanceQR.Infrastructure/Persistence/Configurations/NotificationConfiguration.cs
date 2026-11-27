using AttendanceQR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceQR.Infrastructure.Persistence.Configurations;

// The notification module (Api/Notifications). Every reference to a person or a branch is either Cascade (it goes with
// them) or no foreign key at all (history that outlives them) — never Restrict, so a delete is never held by a
// notification (DeleteRules, RowReferences).

public class NotificationEventConfiguration : IEntityTypeConfiguration<NotificationEvent>
{
    public void Configure(EntityTypeBuilder<NotificationEvent> builder)
    {
        builder.ToTable("NotificationEvents");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Kind).HasMaxLength(64).IsRequired();
        builder.Property(e => e.LastError).HasMaxLength(500);
        // The worker's sweep: what is waiting and due. No unique index on purpose — see NotificationEvent: the outbox
        // row rides in the check-in's own save and must never be the reason it fails.
        builder.HasIndex(e => new { e.TenantId, e.Status, e.NextAttemptUtc });
    }
}

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.TypeKey).HasMaxLength(64).IsRequired();
        builder.Property(n => n.Title).HasMaxLength(120).IsRequired();
        builder.Property(n => n.Body).HasMaxLength(400).IsRequired();
        builder.Property(n => n.SubjectName).HasMaxLength(200);
        builder.Property(n => n.BranchName).HasMaxLength(200);
        builder.Property(n => n.Url).HasMaxLength(300);

        // The duplicate guard: one line per event, recipient and type, whatever retries or overlapping passes do.
        builder.HasIndex(n => new { n.EventId, n.RecipientEmployeeId, n.TypeKey })
            .IsUnique()
            .HasFilter("\"EventId\" IS NOT NULL");
        // The center, newest first, and its cursor.
        builder.HasIndex(n => new { n.TenantId, n.RecipientEmployeeId, n.CreatedAtUtc });
        // The unread badge — only the unread rows are in it, so it stays small however long the history.
        builder.HasIndex(n => new { n.TenantId, n.RecipientEmployeeId })
            .HasFilter("\"ReadAtUtc\" IS NULL")
            .HasDatabaseName("IX_Notifications_Unread");

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(n => n.RecipientEmployeeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationDelivery> builder)
    {
        builder.ToTable("NotificationDeliveries");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.TypeKey).HasMaxLength(64).IsRequired();
        builder.Property(d => d.Title).HasMaxLength(120).IsRequired();
        builder.Property(d => d.Body).HasMaxLength(400).IsRequired();
        builder.Property(d => d.Url).HasMaxLength(300);
        builder.Property(d => d.LastError).HasMaxLength(500);

        // The send/retry sweep.
        builder.HasIndex(d => new { d.TenantId, d.Channel, d.Status, d.NextAttemptUtc });
        // «When did this person last get a push» — the burst throttle.
        builder.HasIndex(d => new { d.TenantId, d.RecipientEmployeeId, d.SentAtUtc });

        builder.HasOne<Notification>()
            .WithMany()
            .HasForeignKey(d => d.NotificationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(d => d.RecipientEmployeeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.ToTable("NotificationPreferences");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Category).HasMaxLength(32).IsRequired();
        builder.Property(p => p.EnabledTypes).HasMaxLength(500).IsRequired();

        builder.HasIndex(p => new { p.TenantId, p.EmployeeId, p.Category }).IsUnique();
        // «Does anybody in this company want attendance notifications at all» — read (cached) before an event is written.
        builder.HasIndex(p => new { p.TenantId, p.Category });

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(p => p.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class NotificationBranchPreferenceConfiguration : IEntityTypeConfiguration<NotificationBranchPreference>
{
    public void Configure(EntityTypeBuilder<NotificationBranchPreference> builder)
    {
        builder.ToTable("NotificationBranchPreferences");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Category).HasMaxLength(32).IsRequired();

        builder.HasIndex(p => new { p.TenantId, p.EmployeeId, p.Category, p.LocationId }).IsUnique();

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(p => p.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);
        // A deleted branch simply drops out of everybody's pick.
        builder.HasOne<Location>()
            .WithMany()
            .HasForeignKey(p => p.LocationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
