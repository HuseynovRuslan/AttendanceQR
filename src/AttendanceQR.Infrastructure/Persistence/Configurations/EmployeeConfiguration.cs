using AttendanceQR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceQR.Infrastructure.Persistence.Configurations;

public class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("Employees");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.FullName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.FirstName)
            .HasMaxLength(100);

        builder.Property(e => e.LastName)
            .HasMaxLength(100);

        builder.Property(e => e.FatherName)
            .HasMaxLength(200);

        builder.Property(e => e.Position)
            .HasMaxLength(200);

        builder.Property(e => e.Email)
            .HasMaxLength(256);

        builder.Property(e => e.PasswordHash)
            .IsRequired();

        builder.Property(e => e.Role)
            .HasConversion<int>();

        // SHA256 base64 hash is 44 chars; give some headroom.
        builder.Property(e => e.InvitationTokenHash)
            .HasMaxLength(128);

        // Photo-audit reference-selfie object key (points into MinIO).
        builder.Property(e => e.ReferencePhotoKey)
            .HasMaxLength(256);

        // Unique PER TENANT, only where an email is present — a partial index so the many phone-only
        // employees (Email NULL) don't collide with each other. NULLs are distinct in Postgres, but
        // the filter also keeps the index small and the intent explicit.
        builder.HasIndex(e => new { e.TenantId, e.Email })
            .IsUnique()
            .HasFilter("\"Email\" IS NOT NULL");

        builder.Property(e => e.PhoneNumber)
            .HasMaxLength(20);

        // Unique per tenant when present — Postgres treats NULLs as distinct, so phone-less rows don't clash.
        builder.HasIndex(e => new { e.TenantId, e.PhoneNumber })
            .IsUnique();

        // Activation lookups are by token hash.
        builder.HasIndex(e => e.InvitationTokenHash);

        // Employee -> Location (required). No navigation on Location; restrict to
        // avoid deleting a location that still has employees.
        builder.HasOne<Location>()
            .WithMany()
            .HasForeignKey(e => e.LocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
