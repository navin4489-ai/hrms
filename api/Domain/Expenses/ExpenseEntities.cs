namespace Wintech.Hrms.Api.Domain.Expenses;

/// <summary>Claim lifecycle (FR-EMP-082). Mirrors the contract's status strings exactly.</summary>
public enum ExpenseClaimStatus
{
    Draft = 0,
    Pending = 1,
    ManagerApproved = 2,
    FinanceApproved = 3,
    Approved = 4,
    Rejected = 5,
    Paid = 6,
    Cancelled = 7,
    Returned = 8
}

public enum TravelRequestStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Cancelled = 3
}

/// <summary>Employee expense claim routed through the approval workflow (FR-EMP-080..082).</summary>
public class ExpenseClaim
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }

    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "INR";
    public DateOnly ExpenseDate { get; set; }
    public string BusinessPurpose { get; set; } = string.Empty;
    public string? CostCentre { get; set; }

    public ExpenseClaimStatus Status { get; set; } = ExpenseClaimStatus.Pending;
    public DateTime SubmittedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    /// <summary>Finance payment reconciliation reference (FR-ADM-101/103).</summary>
    public string? PaidReference { get; set; }
    /// <summary>Populated on rejection; visible to the employee (FR-EMP-084).</summary>
    public string? RejectionReason { get; set; }

    public Guid? ApprovalTaskId { get; set; }

    // Private receipt storage metadata (BR-010: the file itself never leaves PrivateFileStore).
    public string? ReceiptStorageKey { get; set; }
    public string? ReceiptFileName { get; set; }
    public string? ReceiptContentType { get; set; }
    public long? ReceiptSizeBytes { get; set; }
    /// <summary>SHA-256 of the uploaded bytes — duplicate detection (FR-EMP-081).</summary>
    public string? ReceiptSha256 { get; set; }
}

/// <summary>Business travel request with an approval chain (FR-EMP-083, FR-ADM-102).</summary>
public class TravelRequest
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }

    public string Destination { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal EstimatedCost { get; set; }
    public string Currency { get; set; } = "INR";

    public TravelRequestStatus Status { get; set; } = TravelRequestStatus.Pending;
    public DateTime SubmittedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? RejectionReason { get; set; }
    public Guid? ApprovalTaskId { get; set; }
}
