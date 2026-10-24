using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wintech.Hrms.Api.Domain.Attendance;
using Wintech.Hrms.Api.Domain.Employees;

namespace Wintech.Hrms.Api.Data.Configurations;

public sealed class WorkShiftConfiguration : IEntityTypeConfiguration<WorkShift>
{
    public void Configure(EntityTypeBuilder<WorkShift> builder)
    {
        builder.ToTable("att_shifts");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Code).HasMaxLength(50).IsRequired();
        builder.Property(s => s.Name).HasMaxLength(100).IsRequired();
        builder.Property(s => s.WeekDays).HasMaxLength(100).IsRequired();
        builder.HasIndex(s => s.Code).IsUnique();
    }
}

public sealed class ShiftAssignmentConfiguration : IEntityTypeConfiguration<ShiftAssignment>
{
    public void Configure(EntityTypeBuilder<ShiftAssignment> builder)
    {
        builder.ToTable("att_shift_assignments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Note).HasMaxLength(500);
        builder.HasIndex(a => new { a.EmployeeId, a.Date }).IsUnique();
        builder.HasIndex(a => a.Date);

        builder.HasOne(a => a.Employee)
            .WithMany()
            .HasForeignKey(a => a.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Shift)
            .WithMany()
            .HasForeignKey(a => a.ShiftId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AttendanceDayRecordConfiguration : IEntityTypeConfiguration<AttendanceDayRecord>
{
    public void Configure(EntityTypeBuilder<AttendanceDayRecord> builder)
    {
        builder.ToTable("att_daily_records");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.WorkedHours).HasPrecision(6, 2);
        builder.Property(r => r.OvertimeHours).HasPrecision(6, 2);
        builder.Property(r => r.Note).HasMaxLength(500);
        builder.HasIndex(r => new { r.EmployeeId, r.WorkDate }).IsUnique();
        builder.HasIndex(r => r.WorkDate);

        builder.HasOne(r => r.Employee)
            .WithMany()
            .HasForeignKey(r => r.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Shift)
            .WithMany()
            .HasForeignKey(r => r.ShiftId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class AttendanceCorrectionRequestConfiguration : IEntityTypeConfiguration<AttendanceCorrectionRequest>
{
    public void Configure(EntityTypeBuilder<AttendanceCorrectionRequest> builder)
    {
        builder.ToTable("att_corrections");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Reason).HasMaxLength(1000).IsRequired();
        builder.Property(c => c.AttachmentUrl).HasMaxLength(500);
        builder.Property(c => c.DecisionComment).HasMaxLength(1000);
        builder.HasIndex(c => new { c.EmployeeId, c.WorkDate, c.Status });
        builder.HasIndex(c => c.Status);
        builder.HasIndex(c => c.SubmittedAt);

        builder.HasOne(c => c.Employee)
            .WithMany()
            .HasForeignKey(c => c.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ShiftSwapRequestConfiguration : IEntityTypeConfiguration<ShiftSwapRequest>
{
    public void Configure(EntityTypeBuilder<ShiftSwapRequest> builder)
    {
        builder.ToTable("att_swap_requests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Reason).HasMaxLength(1000).IsRequired();
        builder.Property(r => r.DecisionComment).HasMaxLength(1000);
        builder.HasIndex(r => new { r.EmployeeId, r.RosterDate });
        builder.HasIndex(r => r.Status);

        builder.HasOne(r => r.Employee)
            .WithMany()
            .HasForeignKey(r => r.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CompanyHolidayConfiguration : IEntityTypeConfiguration<CompanyHoliday>
{
    public void Configure(EntityTypeBuilder<CompanyHoliday> builder)
    {
        builder.ToTable("att_holidays");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Name).HasMaxLength(200).IsRequired();
        builder.HasIndex(h => h.Date);
    }
}
