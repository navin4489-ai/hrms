using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Wintech.Hrms.Api.Domain.Leave;

namespace Wintech.Hrms.Api.Modules.Leave;

// ------------------------------------------------------------------ reference data

/// <summary>GET /leave-types.</summary>
public sealed record LeaveTypeDto(
    Guid Id,
    string Code,
    string Name,
    bool IsPaid,
    bool RequiresAttachment,
    bool AllowsHalfDay,
    int? MaxConsecutiveDays);

/// <summary>GET /leave-balances.</summary>
public sealed record LeaveBalanceDto(
    Guid LeaveTypeId,
    string Code,
    string Name,
    decimal Available,
    decimal Accrued,
    decimal Used,
    decimal Pending,
    decimal CarryForward,
    string Unit);

/// <summary>POST /admin/leave/balances/adjust — HR/admin credits or sets an employee's balance.</summary>
public sealed class AdjustLeaveBalanceRequest
{
    [System.ComponentModel.DataAnnotations.Required]
    public Guid EmployeeId { get; init; }

    [System.ComponentModel.DataAnnotations.Required]
    public Guid LeaveTypeId { get; init; }

    [System.ComponentModel.DataAnnotations.Range(2000, 2100)]
    public int Year { get; init; }

    /// <summary>Amount to add (negative to debit) in "Add" mode, or the new Accrued value in "Set" mode.</summary>
    [System.ComponentModel.DataAnnotations.Range(typeof(decimal), "-365", "365")]
    public decimal Amount { get; init; }

    /// <summary>"Add" (default) credits/debits accrued; "Set" overwrites accrued.</summary>
    [System.ComponentModel.DataAnnotations.StringLength(10)]
    public string Mode { get; init; } = "Add";

    [System.ComponentModel.DataAnnotations.StringLength(500)]
    public string? Note { get; init; }
}

public sealed record LeaveBalanceAdjustedDto(
    Guid EmployeeId,
    Guid LeaveTypeId,
    int Year,
    decimal Accrued,
    decimal Used,
    decimal Pending,
    decimal CarryForward,
    decimal Available);

/// <summary>GET /leave-policies.</summary>
public sealed record LeavePolicyDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    string AccrualFrequency,
    decimal AnnualEntitlementDays,
    decimal CarryForwardMaxDays,
    bool NegativeBalanceAllowed,
    int NoticeDays,
    bool AttachmentRequired);

public sealed record AdminLeaveTypeDto(
    Guid Id,
    string Code,
    string Name,
    bool IsPaid,
    bool RequiresAttachment,
    bool AllowsHalfDay,
    int? MaxConsecutiveDays,
    string Unit,
    int DisplayOrder,
    bool IsActive);

public sealed record AdminLeavePolicyDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    Guid? LeaveTypeId,
    string AccrualFrequency,
    decimal AnnualEntitlementDays,
    decimal CarryForwardMaxDays,
    bool NegativeBalanceAllowed,
    int NoticeDays,
    bool AttachmentRequired,
    bool IsActive);

public sealed class UpsertLeaveTypeRequest
{
    [Required, StringLength(50)] public string Code { get; init; } = string.Empty;
    [Required, StringLength(100)] public string Name { get; init; } = string.Empty;
    public bool IsPaid { get; init; } = true;
    public bool RequiresAttachment { get; init; }
    public bool AllowsHalfDay { get; init; }
    [Range(1, 365)] public int? MaxConsecutiveDays { get; init; }
    [Required, StringLength(20)] public string Unit { get; init; } = "Days";
    public int DisplayOrder { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed class UpsertLeavePolicyRequest
{
    [Required, StringLength(50)] public string Code { get; init; } = string.Empty;
    [Required, StringLength(100)] public string Name { get; init; } = string.Empty;
    [StringLength(500)] public string? Description { get; init; }
    public Guid? LeaveTypeId { get; init; }
    [Required] public string AccrualFrequency { get; init; } = "Monthly";
    [Range(typeof(decimal), "0", "365")] public decimal AnnualEntitlementDays { get; init; }
    [Range(typeof(decimal), "0", "365")] public decimal CarryForwardMaxDays { get; init; }
    public bool NegativeBalanceAllowed { get; init; }
    [Range(0, 365)] public int NoticeDays { get; init; }
    public bool AttachmentRequired { get; init; }
    public bool IsActive { get; init; } = true;
}

// ------------------------------------------------------------------ requests

public sealed class LeaveRequestCreateDto
{
    [Required]
    public Guid LeaveTypeId { get; set; }

    [Required]
    public DateOnly StartDate { get; set; }

    [Required]
    public DateOnly EndDate { get; set; }

    /// <summary>Full | FirstHalf | SecondHalf.</summary>
    [Required]
    [StringLength(20)]
    public string DayType { get; set; } = "Full";

    [Required]
    [StringLength(1000)]
    public string Reason { get; set; } = string.Empty;

    public string? AttachmentUrl { get; set; }
}

public sealed record LeaveRequestCreatedDto(Guid Id, LeaveRequestStatus Status, decimal Units, Guid ApprovalTaskId);

public sealed class LeaveCancelDto
{
    [Required]
    [StringLength(1000)]
    public string Reason { get; set; } = string.Empty;
}

public sealed record LeaveCancelledDto(string Status);

// ------------------------------------------------------------------ list / detail

public sealed record LeaveRequestListItem(
    Guid Id,
    string LeaveType,
    Guid LeaveTypeId,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal Units,
    LeaveDayType DayType,
    LeaveRequestStatus Status,
    string Reason,
    DateTime SubmittedAt,
    DateTime? DecidedAt,
    string? ApproverName,
    string? RejectionReason,
    Guid? ApprovalTaskId);

public sealed record LeaveDecisionHistoryEntry(string? Step, string Action, string ActorName, string? Comment, DateTime At);

public sealed record LeaveRequestDetail(
    Guid Id,
    string LeaveType,
    Guid LeaveTypeId,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal Units,
    LeaveDayType DayType,
    LeaveRequestStatus Status,
    string Reason,
    string? AttachmentUrl,
    DateTime SubmittedAt,
    DateTime? DecidedAt,
    string? ApproverName,
    string? RejectionReason,
    Guid? ApprovalTaskId,
    IReadOnlyList<LeaveDecisionHistoryEntry> DecisionHistory);

// ------------------------------------------------------------------ calendar

/// <summary>GET /leave/calendar entry (FR-EMP-045). `type`: Leave | Holiday | WeekOff.</summary>
public sealed record LeaveCalendarEntry(
    DateOnly Date,
    string Type,
    string Label,
    LeaveRequestStatus? Status);

// ------------------------------------------------------------------ dashboard / report

public sealed record DashboardLeaveBalanceDto(
    Guid LeaveTypeId,
    string Code,
    string Name,
    decimal Available,
    decimal Used,
    decimal Pending,
    decimal Accrued,
    decimal CarryForward);

public sealed record LeaveLiabilityRow(
    string Department,
    string LeaveType,
    decimal Entitlement,
    decimal Used,
    decimal Pending,
    decimal CarryForward);
