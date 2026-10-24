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
