using AttendanceQR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceQR.Infrastructure.Persistence.Configurations;

public class PinResetRequestConfiguration : IEntityTypeConfiguration<PinResetRequest>
{
    public void Configure(EntityTypeBuilder<PinResetRequest> builder)
    {
        builder.ToTable("PinResetRequests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Status).HasConversion<int>();
        // Two Employee FKs (requester + resolver), no navigation property, Restrict so an employee with
        // a request row can't be deleted out from under it — same rule as DeviceChangeRequest.
        builder.HasOne<Employee>().WithMany().HasForeignKey(r => r.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Employee>().WithMany().HasForeignKey(r => r.ResolvedByEmployeeId).OnDelete(DeleteBehavior.Restrict);
        // Query the pending queue per employee cheaply (dedup check + admin list).
        builder.HasIndex(r => r.EmployeeId);
    }
}
