using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Wintech.Hrms.Api.Domain.Payroll;

// Table names carry the pay_ prefix so payroll tables never collide with other modules
// when the modular monolith is assembled (see AGENT_GUIDE.md, "Table names").
public sealed class PayrollPeriodConfiguration : IEntityTypeConfiguration<PayrollPeriod>
{
    public void Configure(EntityTypeBuilder<PayrollPeriod> builder)
    {
        builder.ToTable("pay_payroll_periods");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Label).HasMaxLength(50).IsRequired();
        builder.HasIndex(p => p.Label).IsUnique();
        builder.HasIndex(p => p.Status);
        builder.HasIndex(p => p.StartDate);
        builder.Property(p => p.LockReason).HasMaxLength(500);
    }
}

public sealed class PayslipConfiguration : IEntityTypeConfiguration<Payslip>
{
    public void Configure(EntityTypeBuilder<Payslip> builder)
    {
        builder.ToTable("pay_payslips");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Currency).HasMaxLength(10).IsRequired();
        builder.Property(p => p.Gross).HasPrecision(18, 2);
        builder.Property(p => p.TotalDeductions).HasPrecision(18, 2);
        builder.Property(p => p.NetPay).HasPrecision(18, 2);
        builder.HasIndex(p => new { p.EmployeeId, p.Status });
        builder.HasIndex(p => p.PeriodId);

        builder.HasOne(p => p.Period)
            .WithMany()
            .HasForeignKey(p => p.PeriodId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Employee)
            .WithMany()
            .HasForeignKey(p => p.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PayslipComponentConfiguration : IEntityTypeConfiguration<PayslipComponent>
{
    public void Configure(EntityTypeBuilder<PayslipComponent> builder)
    {
        builder.ToTable("pay_payslip_components");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Component).HasMaxLength(100).IsRequired();
        builder.Property(c => c.Amount).HasPrecision(18, 2);
        builder.HasIndex(c => c.PayslipId);

        builder.HasOne(c => c.Payslip)
            .WithMany(p => p.Components)
            .HasForeignKey(c => c.PayslipId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class EmployeeSalaryStructureConfiguration : IEntityTypeConfiguration<EmployeeSalaryStructure>
{
    public void Configure(EntityTypeBuilder<EmployeeSalaryStructure> builder)
    {
        builder.ToTable("pay_employee_salary_structures");
        builder.HasKey(s => s.Id);
        // One pay record per employee: finance edits it in place.
        builder.HasIndex(s => s.EmployeeId).IsUnique();
        builder.Property(s => s.Currency).HasMaxLength(10).IsRequired();
        foreach (var property in new[]
                 {
                     nameof(EmployeeSalaryStructure.BasicAnnual), nameof(EmployeeSalaryStructure.HraAnnual),
                     nameof(EmployeeSalaryStructure.DaAnnual), nameof(EmployeeSalaryStructure.SpecialAllowanceAnnual),
                     nameof(EmployeeSalaryStructure.OtherAllowanceAnnual), nameof(EmployeeSalaryStructure.EmployerPfAnnual),
                     nameof(EmployeeSalaryStructure.GratuityAnnual), nameof(EmployeeSalaryStructure.ProfessionalTaxMonthly),
                     nameof(EmployeeSalaryStructure.Deduction80CAnnual), nameof(EmployeeSalaryStructure.Deduction80DAnnual),
                     nameof(EmployeeSalaryStructure.HomeLoanInterestAnnual), nameof(EmployeeSalaryStructure.OtherDeductionsAnnual)
                 })
            builder.Property(property).HasPrecision(18, 2);

        builder.HasOne(s => s.Employee)
            .WithMany()
            .HasForeignKey(s => s.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
