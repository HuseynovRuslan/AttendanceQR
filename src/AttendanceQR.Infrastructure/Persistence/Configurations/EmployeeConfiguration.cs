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

        builder.Property(e => e.FatherName)
            .HasMaxLength(200);

        builder.Property(e => e.Position)
            .HasMaxLength(200);

        builder.Property(e => e.Email)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.PasswordHash)
            .IsRequired();

        builder.Property(e => e.Role)
            .HasConversion<int>();

        // SHA256 base64 hash is 44 chars; give some headroom.
        builder.Property(e => e.InvitationTokenHash)
            .HasMaxLength(128);

        builder.HasIndex(e => e.Email)
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
