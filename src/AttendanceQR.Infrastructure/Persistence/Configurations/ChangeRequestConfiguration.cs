using AttendanceQR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceQR.Infrastructure.Persistence.Configurations;

public class ChangeRequestConfiguration : IEntityTypeConfiguration<ChangeRequest>
{
    public void Configure(EntityTypeBuilder<ChangeRequest> builder)
    {
        builder.ToTable("ChangeRequests", t =>
        {
            t.HasCheckConstraint("CK_ChangeRequests_Status", "\"Status\" IN (0, 1, 2, 3)");
            // A pending request has no decision. A final one has its decider, its time and, when approved, the
            // moment it was applied, which is written in the same transaction as the approval itself.
            t.HasCheckConstraint("CK_ChangeRequests_Decision",
                "(\"Status\" = 0 AND \"DecidedAtUtc\" IS NULL AND \"DecidedByEmployeeId\" IS NULL AND \"AppliedAtUtc\" IS NULL)"
                + " OR (\"Status\" IN (2, 3) AND \"DecidedAtUtc\" IS NOT NULL AND \"DecidedByEmployeeId\" IS NOT NULL AND \"AppliedAtUtc\" IS NULL)"
                + " OR (\"Status\" = 1 AND \"DecidedAtUtc\" IS NOT NULL AND \"DecidedByEmployeeId\" IS NOT NULL AND \"AppliedAtUtc\" IS NOT NULL)");
            // A rejection without a reason is not a decision anybody can act on.
            t.HasCheckConstraint("CK_ChangeRequests_RejectReason",
                "\"Status\" <> 2 OR length(btrim(coalesce(\"DecisionNote\", ''))) > 0");
            // A direct change was made, not asked for, so it can only ever be Approved.
            t.HasCheckConstraint("CK_ChangeRequests_Direct", "NOT \"Direct\" OR \"Status\" = 1");
            // A real request always has the number people refer to it by.
            t.HasCheckConstraint("CK_ChangeRequests_Number", "\"Direct\" OR \"Number\" IS NOT NULL");
        });
        builder.HasKey(r => r.Id);
        // The principal key of the history lines, so a line cannot point at another company's request.
        builder.HasAlternateKey(r => new { r.TenantId, r.Id });

        builder.Property(r => r.Type).HasConversion<int>();
        builder.Property(r => r.Status).HasConversion<int>();
        builder.Property(r => r.Payload).IsRequired().HasColumnType("jsonb");
        builder.Property(r => r.Note).HasMaxLength(500);
        builder.Property(r => r.DecisionNote).HasMaxLength(500);
        builder.Property(r => r.DedupeKey).HasMaxLength(200);
        builder.Property(r => r.IdempotencyKey).HasMaxLength(100);
        builder.Property(r => r.Version).IsConcurrencyToken();

        // The branch and the structure carry the company, so a request cannot name another company's branch,
        // whether the code or a hand-written UPDATE tries it. Restrict: a branch with requests is archived, not
        // deleted, and a structure is never deleted.
        builder.HasOne<Location>().WithMany()
            .HasForeignKey(r => new { r.TenantId, r.LocationId })
            .HasPrincipalKey(l => new { l.TenantId, l.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Structure>().WithMany()
            .HasForeignKey(r => new { r.TenantId, r.StructureIdAtCreation })
            .HasPrincipalKey(s => new { s.TenantId, s.Id })
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
        // SetNull: deleting an account must never be blocked by the request that made it. The payload keeps the
        // name, and the history keeps every line.
        builder.HasOne<Employee>().WithMany()
            .HasForeignKey(r => r.EmployeeId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(r => new { r.TenantId, r.Number }).IsUnique();
        // The approver's queue: this company's pending requests by branch, oldest first.
        builder.HasIndex(r => new { r.TenantId, r.Status, r.LocationId, r.CreatedAtUtc });
        // «Müraciətlərim»: one person's requests, newest first.
        builder.HasIndex(r => new { r.TenantId, r.RequestedByEmployeeId, r.CreatedAtUtc });
        builder.HasIndex(r => new { r.TenantId, r.EmployeeId }).HasFilter("\"EmployeeId\" IS NOT NULL");
        // A retried press of «Göndər» finds the first one instead of making a second.
        builder.HasIndex(r => new { r.TenantId, r.IdempotencyKey }).IsUnique()
            .HasFilter("\"IdempotencyKey\" IS NOT NULL");
        // The same thing cannot be asked twice while the first is still waiting. Two timekeepers sending the same
        // person at once get one request and one 23505. The code checks first as well, because the in-memory
        // test provider does not enforce this.
        builder.HasIndex(r => new { r.TenantId, r.Type, r.DedupeKey }).IsUnique()
            .HasFilter("\"Status\" = 0 AND \"DedupeKey\" IS NOT NULL")
            .HasDatabaseName("IX_ChangeRequests_OnePending");
    }
}
