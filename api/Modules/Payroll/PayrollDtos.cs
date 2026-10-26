using Wintech.Hrms.Api.Domain.Payroll;

namespace Wintech.Hrms.Api.Modules.Payroll;

/// <summary>ListItem for GET /payslips (contract §10).</summary>
public sealed record PayslipListItem(
    Guid Id,
    Guid PeriodId,
    string PeriodLabel,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal Gross,
    decimal NetPay,
    string Currency,
    PayslipStatus Status,
    DateTime? PublishedAt);

/// <summary>One earning or deduction line (contract §10 detail shape).</summary>
public sealed record PayslipComponentDto(string Component, decimal Amount);

/// <summary>Full breakdown for GET /payslips/{id} (contract §10).</summary>
public sealed record PayslipDetailDto(
    Guid Id,
    string PeriodLabel,
    DateOnly StartDate,
    DateOnly EndDate,
    string Currency,
    IReadOnlyList<PayslipComponentDto> Earnings,
    IReadOnlyList<PayslipComponentDto> Deductions,
    decimal Gross,
    decimal TotalDeductions,
    decimal NetPay,
    decimal? YtdGross,
    string EmployeeNumber,
    string EmployeeName,
    string LegalEntity,
    DateTime GeneratedAt);

/// <summary>ListItem for GET /payroll-periods (contract §10).</summary>
public sealed record PayrollPeriodDto(
    Guid Id,
    string Label,
    DateOnly StartDate,
    DateOnly EndDate,
    PayrollPeriodStatus Status,
    DateOnly CutOffDate,
    DateTime? LockedAt);

/// <summary>Request to configure an open payroll period.</summary>
public sealed class CreatePayrollPeriodRequest
{
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public DateOnly CutOffDate { get; init; }
    [System.ComponentModel.DataAnnotations.StringLength(50)]
    public string? Label { get; init; }
}

/// <summary>Body for POST /payroll-periods/{id}/lock and .../unlock (reason required, FR-ADM-075).</summary>
public sealed class PayrollPeriodActionRequest
{
    /// <summary>Why the period is being locked/unlocked. Required — unlocks are audited (FR-ADM-075).</summary>
    [System.ComponentModel.DataAnnotations.Required(AllowEmptyStrings = false)]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Generated PDF payload returned by the payslip download endpoint.</summary>
public sealed record PdfDownload(byte[] Content, string FileName);
