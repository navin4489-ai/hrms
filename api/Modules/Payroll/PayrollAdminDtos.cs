using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Wintech.Hrms.Api.Domain.Payroll;

namespace Wintech.Hrms.Api.Modules.Payroll;

public sealed class PayrollPayslipImportRequest
{
    [Required, MinLength(1), MaxLength(1000)]
    public List<PayrollPayslipImportRow> Rows { get; init; } = [];
}

public sealed class PayrollPayslipImportRow
{
    [Required, StringLength(50)]
    public string EmployeeNumber { get; init; } = string.Empty;

    [Range(typeof(decimal), "0", "999999999999")]
    public decimal Gross { get; init; }

    [Range(typeof(decimal), "0", "999999999999")]
    public decimal TotalDeductions { get; init; }

    [Range(typeof(decimal), "0", "999999999999")]
    public decimal NetPay { get; init; }

    [StringLength(10)]
    public string Currency { get; init; } = "INR";

    [MaxLength(100)]
    public List<PayrollPayslipComponentInput> Components { get; init; } = [];
}

public sealed class PayrollPayslipComponentInput
{
    [Required, StringLength(100)]
    public string Component { get; init; } = string.Empty;

    [Range(typeof(decimal), "0", "999999999999")]
    public decimal Amount { get; init; }

    [Required]
    public PayslipComponentKind? Kind { get; init; }
}

public sealed record PayrollImportRowResult(
    int Row,
    string EmployeeNumber,
    bool Valid,
    IReadOnlyList<string> Errors);

public sealed record PayrollImportPreviewResult(
    Guid PeriodId,
    string PeriodLabel,
    int TotalRows,
    int ValidRows,
    IReadOnlyList<PayrollImportRowResult> Rows);

public sealed record PayrollPublishResult(Guid PeriodId, int PublishedCount, DateTime PublishedAt);

public sealed record PayrollAdminPayslipRow(
    Guid Id,
    string EmployeeNumber,
    string EmployeeName,
    decimal Gross,
    decimal TotalDeductions,
    decimal NetPay,
    string Currency,
    string Status,
    DateTime GeneratedAt,
    DateTime? PublishedAt);

// ------------------------------------------------------------------ salary structures & automatic tax

/// <summary>Finance-maintained pay details for one employee with the computed tax preview.</summary>
public sealed record AdminSalaryStructureDto(
    Guid EmployeeId,
    string EmployeeNumber,
    string EmployeeName,
    string? Department,
    decimal BasicAnnual,
    decimal HraAnnual,
    decimal DaAnnual,
    decimal SpecialAllowanceAnnual,
    decimal OtherAllowanceAnnual,
    decimal EmployerPfAnnual,
    decimal GratuityAnnual,
    decimal ProfessionalTaxMonthly,
    string TaxRegime,
    decimal Deduction80CAnnual,
    decimal Deduction80DAnnual,
    decimal HomeLoanInterestAnnual,
    decimal OtherDeductionsAnnual,
    decimal AnnualCtc,
    decimal MonthlyGross,
    IncomeTaxEstimateDto Tax,
    DateTime? UpdatedAt);

/// <summary>Write body for creating or replacing an employee's pay details.</summary>
public sealed class SaveSalaryStructureRequest
{
    [System.ComponentModel.DataAnnotations.Range(typeof(decimal), "0", "999999999999")]
    public decimal BasicAnnual { get; init; }

    [System.ComponentModel.DataAnnotations.Range(typeof(decimal), "0", "999999999999")]
    public decimal HraAnnual { get; init; }

    [System.ComponentModel.DataAnnotations.Range(typeof(decimal), "0", "999999999999")]
    public decimal DaAnnual { get; init; }

    [System.ComponentModel.DataAnnotations.Range(typeof(decimal), "0", "999999999999")]
    public decimal SpecialAllowanceAnnual { get; init; }

    [System.ComponentModel.DataAnnotations.Range(typeof(decimal), "0", "999999999999")]
    public decimal OtherAllowanceAnnual { get; init; }

    [System.ComponentModel.DataAnnotations.Range(typeof(decimal), "0", "999999999999")]
    public decimal EmployerPfAnnual { get; init; }

    [System.ComponentModel.DataAnnotations.Range(typeof(decimal), "0", "999999999999")]
    public decimal GratuityAnnual { get; init; }

    /// <summary>Monthly professional tax (₹0–2500; states without PT use 0).</summary>
    [System.ComponentModel.DataAnnotations.Range(typeof(decimal), "0", "2500")]
    public decimal ProfessionalTaxMonthly { get; init; } = 200m;

    [System.ComponentModel.DataAnnotations.StringLength(10)]
    public string TaxRegime { get; init; } = "New";

    [System.ComponentModel.DataAnnotations.Range(typeof(decimal), "0", "999999999999")]
    public decimal Deduction80CAnnual { get; init; }

    [System.ComponentModel.DataAnnotations.Range(typeof(decimal), "0", "999999999999")]
    public decimal Deduction80DAnnual { get; init; }

    [System.ComponentModel.DataAnnotations.Range(typeof(decimal), "0", "999999999999")]
    public decimal HomeLoanInterestAnnual { get; init; }

    [System.ComponentModel.DataAnnotations.Range(typeof(decimal), "0", "999999999999")]
    public decimal OtherDeductionsAnnual { get; init; }
}

/// <summary>Computed annual tax shown while finance edits pay details (before or after saving).</summary>
public sealed record IncomeTaxEstimateDto(
    string Regime,
    int FinancialYear,
    decimal AnnualGross,
    decimal StandardDeduction,
    decimal ChapterViADeductions,
    decimal TaxableIncome,
    decimal TaxBeforeRebate,
    decimal Rebate87A,
    decimal Cess,
    decimal AnnualTax,
    decimal MonthlyTds);

/// <summary>Result of POST /payroll-periods/{id}/generate — automatic payslip calculation.</summary>
public sealed record PayrollGenerateResult(
    Guid PeriodId,
    string PeriodLabel,
    int Generated,
    int Skipped,
    IReadOnlyList<string> SkippedEmployees);
