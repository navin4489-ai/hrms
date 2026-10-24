using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Wintech.Hrms.Api.Modules.Expenses;

// ------------------------------------------------------------------ request forms

/// <summary>Multipart form for POST /expenses (FR-EMP-080): fields + receipt file.</summary>
public sealed class CreateExpenseForm
{
    [Required, StringLength(50)]
    public string Category { get; set; } = string.Empty;

    [Required, Range(typeof(decimal), "0.01", "99999999")]
    public decimal Amount { get; set; }

    [StringLength(3)]
    public string? Currency { get; set; }

    [Required]
    public DateOnly ExpenseDate { get; set; }

    [Required, StringLength(500)]
    public string BusinessPurpose { get; set; } = string.Empty;

    [StringLength(50)]
    public string? CostCentre { get; set; }

    public IFormFile? Receipt { get; set; }
}

public sealed class CancelExpenseRequest
{
    [Required, StringLength(500)]
    public string Reason { get; set; } = string.Empty;
}

public sealed class CreateTravelRequestForm
{
    [Required, StringLength(200)]
    public string Destination { get; set; } = string.Empty;

    [Required, StringLength(500)]
    public string Purpose { get; set; } = string.Empty;

    [Required]
    public DateOnly StartDate { get; set; }

    [Required]
    public DateOnly EndDate { get; set; }

    [Required, Range(typeof(decimal), "0", "99999999")]
    public decimal EstimatedCost { get; set; }

    [StringLength(3)]
    public string? Currency { get; set; }
}

// ------------------------------------------------------------------ service inputs

/// <summary>Validated input handed to the service (receipt already buffered in memory).</summary>
public sealed record CreateExpenseInput(
    string Category,
    decimal Amount,
    string Currency,
    DateOnly ExpenseDate,
    string BusinessPurpose,
    string? CostCentre,
    byte[] ReceiptBytes,
    string ReceiptFileName);

public sealed record CreateTravelRequestInput(
    string Destination,
    string Purpose,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal EstimatedCost,
    string Currency);

public sealed class ExpenseQuery
{
    /// <summary>Status name (Draft..Returned); matched case-insensitively.</summary>
    public string? Status { get; set; }
    /// <summary>Submission month filter, YYYY-MM.</summary>
    public string? Month { get; set; }
    /// <summary>true = claims of the caller's direct reports (privileged callers only).</summary>
    public bool Team { get; set; }
}

// ------------------------------------------------------------------ responses

public sealed class ExpenseCreatedResult
{
    public Guid Id { get; init; }
    public Domain.Expenses.ExpenseClaimStatus Status { get; init; }
    public Guid ApprovalTaskId { get; init; }
}

public sealed record ExpenseListItem
{
    public Guid Id { get; init; }
    public string Category { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "INR";
    public DateOnly ExpenseDate { get; init; }
    public string BusinessPurpose { get; init; } = string.Empty;
    public string? CostCentre { get; init; }
    public Domain.Expenses.ExpenseClaimStatus Status { get; init; }
    public DateTime SubmittedAt { get; init; }
    public DateTime? ApprovedAt { get; init; }
    public string? PaidReference { get; init; }
    public string? RejectionReason { get; init; }
    public Guid? ApprovalTaskId { get; init; }
    /// <summary>Private download link — only for the owner or privileged callers.</summary>
    public string? ReceiptUrl { get; init; }
}

public sealed class TravelCreatedResult
{
    public Guid Id { get; init; }
    public Domain.Expenses.TravelRequestStatus Status { get; init; }
    public Guid ApprovalTaskId { get; init; }
}

public sealed record TravelListItem
{
    public Guid Id { get; init; }
    public string Destination { get; init; } = string.Empty;
    public string Purpose { get; init; } = string.Empty;
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public decimal EstimatedCost { get; init; }
    public string Currency { get; init; } = "INR";
    public Domain.Expenses.TravelRequestStatus Status { get; init; }
    public DateTime SubmittedAt { get; init; }
    public DateTime? DecidedAt { get; init; }
    public string? RejectionReason { get; init; }
    public Guid? ApprovalTaskId { get; init; }
    public string? EmployeeName { get; init; }
}
