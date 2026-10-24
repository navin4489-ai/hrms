using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wintech.Hrms.Api.Domain.Employees;
using Wintech.Hrms.Api.Domain.Leave;

namespace Wintech.Hrms.Api.Data.Configurations;

public sealed class LeaveTypeConfiguration : IEntityTypeConfiguration<LeaveType>
{
    public void Configure(EntityTypeBuilder<LeaveType> builder)
    {
        builder.ToTable("lv_leave_types");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Code).HasMaxLength(20).IsRequired();
        builder.Property(t => t.Name).HasMaxLength(100).IsRequired();
        builder.Property(t => t.Unit).HasMaxLength(20).IsRequired();
        builder.HasIndex(t => t.Code).IsUnique();
    }
}

public sealed class LeavePolicyConfiguration : IEntityTypeConfiguration<LeavePolicy>
{
    public void Configure(EntityTypeBuilder<LeavePolicy> builder)
    {
        builder.ToTable("lv_leave_policies");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Code).HasMaxLength(30).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(1000);
        builder.Property(p => p.AnnualEntitlementDays).HasPrecision(6, 2);
        builder.Property(p => p.CarryForwardMaxDays).HasPrecision(6, 2);
        builder.HasIndex(p => p.Code).IsUnique();

        builder.HasOne(p => p.LeaveType)
            .WithMany()
            .HasForeignKey(p => p.LeaveTypeId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class LeaveBalanceConfiguration : IEntityTypeConfiguration<LeaveBalance>
{
    public void Configure(EntityTypeBuilder<LeaveBalance> builder)
    {
        builder.ToTable("lv_leave_balances");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Accrued).HasPrecision(6, 2);
        builder.Property(b => b.Used).HasPrecision(6, 2);
        builder.Property(b => b.Pending).HasPrecision(6, 2);
        builder.Property(b => b.CarryForward).HasPrecision(6, 2);
        builder.HasIndex(b => new { b.EmployeeId, b.LeaveTypeId, b.Year }).IsUnique();

        builder.Ignore(b => b.Available);

        builder.HasOne(b => b.Employee)
            .WithMany()
            .HasForeignKey(b => b.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.LeaveType)
            .WithMany()
            .HasForeignKey(b => b.LeaveTypeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class LeaveRequestConfiguration : IEntityTypeConfiguration<LeaveRequest>
{
    public void Configure(EntityTypeBuilder<LeaveRequest> builder)
    {
        builder.ToTable("lv_leave_requests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Reason).HasMaxLength(1000).IsRequired();
        builder.Property(r => r.AttachmentUrl).HasMaxLength(500);
        builder.Property(r => r.ApproverName).HasMaxLength(200);
        builder.Property(r => r.RejectionReason).HasMaxLength(1000);
        builder.Property(r => r.Units).HasPrecision(6, 2);
        builder.HasIndex(r => new { r.EmployeeId, r.Status });
        builder.HasIndex(r => new { r.StartDate, r.EndDate });
        builder.HasIndex(r => r.Status);
        builder.HasIndex(r => r.SubmittedAt);

        builder.HasOne(r => r.Employee)
            .WithMany()
            .HasForeignKey(r => r.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.LeaveType)
            .WithMany()
            .HasForeignKey(r => r.LeaveTypeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
