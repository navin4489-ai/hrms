using Wintech.Hrms.Api.Domain.Employees;

namespace Wintech.Hrms.Api.Domain.Payroll;

/// <summary>Payroll period lifecycle (FR-ADM-071/075). Closed periods are final.</summary>
public enum PayrollPeriodStatus
{
    /// <summary>Inputs may still change; payslips may be generated/published.</summary>
    Open = 0,
    /// <summary>Locked after approval; reopening requires payroll.manage, a reason and is audited (FR-ADM-075).</summary>
    Locked = 1,
    /// <summary>Finalised for the financial year; cannot be reopened.</summary>
    Closed = 2
}

public enum PayslipStatus
{
    Draft = 0,
    Published = 1
}

public enum PayslipComponentKind
{
    Earning = 0,
    Deduction = 1
}

/// <summary>One payroll run window (contract §10). Payroll calculation itself is out of scope (BRD R-02).</summary>
public class PayrollPeriod
{
    public Guid Id { get; set; }
    /// <summary>Human label, e.g. "Oct 2026".</summary>
    public string Label { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public PayrollPeriodStatus Status { get; set; } = PayrollPeriodStatus.Open;
    /// <summary>Last date for attendance/leave/variable-pay inputs (FR-ADM-071).</summary>
    public DateOnly CutOffDate { get; set; }
    public DateTime? LockedAt { get; set; }
    public Guid? LockedByUserId { get; set; }
    public string? LockReason { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Imported/generated payroll output published to one employee for one period (FR-ADM-074).</summary>
public class Payslip
{
    public Guid Id { get; set; }
    public Guid PeriodId { get; set; }
    public PayrollPeriod? Period { get; set; }
    public Guid EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal Gross { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal NetPay { get; set; }
    public string Currency { get; set; } = "INR";
    public PayslipStatus Status { get; set; } = PayslipStatus.Draft;
    public DateTime? PublishedAt { get; set; }
    public DateTime GeneratedAt { get; set; }
    public ICollection<PayslipComponent> Components { get; set; } = [];
}

/// <summary>A single earning or deduction line on a payslip.</summary>
public class PayslipComponent
{
    public Guid Id { get; set; }
    public Guid PayslipId { get; set; }
    public Payslip? Payslip { get; set; }
    public string Component { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public PayslipComponentKind Kind { get; set; }
}

/// <summary>Income-tax regime used for the automatic TDS computation.</summary>
public enum TaxRegime
{
    /// <summary>Default: concessional slabs, standard deduction, no chapter-VI-A deductions.</summary>
    New = 0,
    /// <summary>Classic slabs with 80C/80D/home-loan deductions and HRA-style declarations.</summary>
    Old = 1
}

/// <summary>
/// Finance-entered annual pay details for one employee. Drives automatic income-tax
/// (TDS) estimation and payslip generation. Amounts are annual INR unless stated.
/// </summary>
public class EmployeeSalaryStructure
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public decimal BasicAnnual { get; set; }
    public decimal HraAnnual { get; set; }
    public decimal DaAnnual { get; set; }
    public decimal SpecialAllowanceAnnual { get; set; }
    public decimal OtherAllowanceAnnual { get; set; }

    /// <summary>Employer PF contribution — part of CTC but never deducted from gross pay.</summary>
    public decimal EmployerPfAnnual { get; set; }

    /// <summary>Gratuity accrual — CTC information only, not part of monthly gross.</summary>
    public decimal GratuityAnnual { get; set; }

    /// <summary>State professional tax per month (₹0–2500; 0 where the state levies none).</summary>
    public decimal ProfessionalTaxMonthly { get; set; } = 200m;

    public TaxRegime TaxRegime { get; set; } = TaxRegime.New;

    // Chapter VI-A declarations (old regime only; capped by the tax calculator).
    public decimal Deduction80CAnnual { get; set; }
    public decimal Deduction80DAnnual { get; set; }
    public decimal HomeLoanInterestAnnual { get; set; }
    public decimal OtherDeductionsAnnual { get; set; }

    public string Currency { get; set; } = "INR";
    public DateTime UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}
