using Wintech.Hrms.Api.Domain.Employees;

namespace Wintech.Hrms.Api.Domain.Leave;

/// <summary>Lifecycle of a leave request (docs/API_CONTRACT.md §8; Draft reserved for future).</summary>
public enum LeaveRequestStatus
{
    Draft = 0,
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4,
    CancelledByEmployee = 5,
    /// <summary>Sent back by the approver for changes (approval action Return).</summary>
    Returned = 6
}

/// <summary>Day granularity of a leave request (FR-EMP-041).</summary>
public enum LeaveDayType
{
    Full = 0,
    FirstHalf = 1,
    SecondHalf = 2
}

/// <summary>How often a policy accrues entitlement (FR-ADM-050).</summary>
public enum LeaveAccrualFrequency
{
    None = 0,
    Monthly = 1,
    Quarterly = 2,
    Annual = 3,
    OnDemand = 4
}

/// <summary>Configured leave type: CL, SL, EL, LWP… (FR-ADM-050).</summary>
public class LeaveType
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsPaid { get; set; } = true;
    public bool RequiresAttachment { get; set; }
    public bool AllowsHalfDay { get; set; }
    public int? MaxConsecutiveDays { get; set; }
    public bool IsActive { get; set; } = true;
    public string Unit { get; set; } = "Days";
    public int DisplayOrder { get; set; }
}

/// <summary>Policy rules that govern requests and balances (FR-ADM-050/051, FR-EMP-046/047).</summary>
public class LeavePolicy
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>Null = applies to every leave type; otherwise binds the policy to one type.</summary>
    public Guid? LeaveTypeId { get; set; }
    public LeaveType? LeaveType { get; set; }
    public LeaveAccrualFrequency AccrualFrequency { get; set; } = LeaveAccrualFrequency.Monthly;
    public decimal AnnualEntitlementDays { get; set; }
    public decimal CarryForwardMaxDays { get; set; }
    public bool NegativeBalanceAllowed { get; set; }
    public int NoticeDays { get; set; }
    public bool AttachmentRequired { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Per-employee, per-type, per-year balance ledger (FR-EMP-040, FR-ADM-053/054).</summary>
public class LeaveBalance
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public Guid LeaveTypeId { get; set; }
    public LeaveType? LeaveType { get; set; }
    public int Year { get; set; }
    public decimal Accrued { get; set; }
    public decimal Used { get; set; }
    /// <summary>Units held by pending requests; released on reject/cancel, converted to Used on approval.</summary>
    public decimal Pending { get; set; }
    public decimal CarryForward { get; set; }

    public decimal Available => Accrued + CarryForward - Used - Pending;
}

/// <summary>Employee leave request routed through the approval engine (FR-EMP-041..044).</summary>
public class LeaveRequest
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public Guid LeaveTypeId { get; set; }
    public LeaveType? LeaveType { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public LeaveDayType DayType { get; set; } = LeaveDayType.Full;
    /// <summary>Paid units consumed (0.5 for half days; working days excluding weekends/holidays).</summary>
    public decimal Units { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? AttachmentUrl { get; set; }
    public LeaveRequestStatus Status { get; set; } = LeaveRequestStatus.Pending;
    public Guid? ApprovalTaskId { get; set; }
    public DateTime SubmittedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? ApproverName { get; set; }
    public string? RejectionReason { get; set; }
    public Guid CreatedByUserId { get; set; }
}
