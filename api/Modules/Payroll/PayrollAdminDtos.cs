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
