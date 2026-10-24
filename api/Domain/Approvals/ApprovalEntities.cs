using Wintech.Hrms.Api.Common.Registration;

namespace Wintech.Hrms.Api.Domain.Approvals;

public enum ApprovalStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Returned = 3,
    Cancelled = 4
}

public enum ApprovalActionType
{
    Start = 0,
    Approve = 1,
    Reject = 2,
    Return = 3,
    Delegate = 4,
    AutoEscalate = 5,
    Cancel = 6
}

public enum ApproverType
{
    /// <summary>Requester's line manager.</summary>
    Manager = 0,
    /// <summary>Any active user holding the named role (approverValue = role name).</summary>
    Role = 1,
    /// <summary>A specific user id (approverValue = user id).</summary>
    NamedUser = 2,
    /// <summary>Manager, falling back to the named role when the requester has no manager.</summary>
    SelfAndManager = 3
}

/// <summary>Versioned workflow definition (FR-ADM-060/064, BR-003).</summary>
public class ApprovalWorkflowDefinition
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>Matches ApprovalTask.RequestType (Leave, AttendanceCorrection, ...).</summary>
    public string RequestType { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public ICollection<ApprovalWorkflowStep> Steps { get; set; } = [];
}

public class ApprovalWorkflowStep
{
    public Guid Id { get; set; }
    public Guid WorkflowId { get; set; }
    public ApprovalWorkflowDefinition? Workflow { get; set; }
    public int StepNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public ApproverType ApproverType { get; set; }
    /// <summary>Role name, user id or fallback role depending on <see cref="ApproverType"/>.</summary>
    public string? ApproverValue { get; set; }
    public int? SLADays { get; set; }
}

/// <summary>
/// One in-flight approval spanning zero or more steps. Snapshot of the workflow version used so
/// policy changes never corrupt in-flight requests (FR-ADM-064).
/// </summary>
public class ApprovalTask
{
    public Guid Id { get; set; }
    /// <summary>Domain transaction type: Leave, AttendanceCorrection, Expense, Travel, Letter, ProfileChange, ShiftSwap, Resignation, Other.</summary>
    public string RequestType { get; set; } = string.Empty;
    public Guid RequestId { get; set; }
    public string Title { get; set; } = string.Empty;
    public Guid RequesterUserId { get; set; }
    public Guid? RequesterEmployeeId { get; set; }
    public string? RequesterName { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public string Status { get; set; } = nameof(ApprovalStatus.Pending);
    public int CurrentStepNumber { get; set; } = 1;
    public string CurrentStepName { get; set; } = string.Empty;
    public Guid? CurrentAssigneeUserId { get; set; }
    public string? CurrentAssigneeRole { get; set; }
    public Guid? DelegatedToUserId { get; set; }
    /// <summary>When the current delegation was granted (FR-MGR-003).</summary>
    public DateTime? DelegatedAt { get; set; }
    /// <summary>Server-side expiry of the delegation; the delegation is void after this instant.</summary>
    public DateTime? DelegationExpiresAt { get; set; }
    public Guid? WorkflowDefinitionId { get; set; }
    public int WorkflowVersion { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DueAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    /// <summary>Optimistic concurrency token for stale/duplicate decision protection (FR-MGR-007).</summary>
    public int Version { get; set; }
    public ICollection<ApprovalAction> Actions { get; set; } = [];
}

public class ApprovalAction
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public ApprovalTask? Task { get; set; }
    public int StepNumber { get; set; }
    public ApprovalActionType Action { get; set; }
    public Guid? ActorUserId { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public Guid? FromUserId { get; set; }
    public Guid? ToUserId { get; set; }
    public DateTime At { get; set; }
}

/// <summary>Input for starting a workflow on a domain transaction.</summary>
public sealed record ApprovalStartRequest(
    string RequestType,
    Guid RequestId,
    string Title,
    Guid RequesterUserId,
    Guid? RequesterEmployeeId,
    string? RequesterName,
    decimal? Amount = null,
    string? Currency = null);

/// <summary>
/// Implemented once per request type. The approval engine invokes it when a workflow completes,
/// so the domain record (LeaveRequest, ExpenseClaim, ...) is updated exactly once (UAT-004).
/// Register by implementing this interface with <see cref="IAppService"/> — no manual DI wiring.
/// </summary>
public interface IApprovalHandler : IAppService
{
    /// <summary>Must match the RequestType the handler serves (e.g. "Leave").</summary>
    string RequestType { get; }

    Task OnCompletedAsync(ApprovalTask task, ApprovalActionType finalAction, string? comment,
        CancellationToken cancellationToken = default);

    /// <summary>Called when the requester cancels a still-pending request.</summary>
    Task OnCancelledAsync(ApprovalTask task, string? reason, CancellationToken cancellationToken = default);
}

/// <summary>Central approval engine — FR-MGR-001..007, FR-ADM-060..065, BR-003/004/007.</summary>
public interface IApprovalService : IAppService
{
    Task<ApprovalTask> StartAsync(ApprovalStartRequest request, CancellationToken cancellationToken = default);

    Task<ApprovalTask> DecideAsync(Guid taskId, ApprovalActionType action, string? comment,
        Guid actorUserId, CancellationToken cancellationToken = default);

    Task<ApprovalTask> DelegateAsync(Guid taskId, Guid toUserId, string? reason,
        Guid actorUserId, CancellationToken cancellationToken = default);

    Task CancelAsync(string requestType, Guid requestId, string? reason,
        CancellationToken cancellationToken = default);

    Task<ApprovalTask?> GetForAsync(string requestType, Guid requestId, CancellationToken cancellationToken = default);
}

/// <summary>Canonical request types understood by the engine (contract section 9).</summary>
public static class ApprovalRequestTypes
{
    public const string Leave = "Leave";
    public const string AttendanceCorrection = "AttendanceCorrection";
    public const string Expense = "Expense";
    public const string Travel = "Travel";
    public const string Letter = "Letter";
    public const string ProfileChange = "ProfileChange";
    public const string ShiftSwap = "ShiftSwap";
    public const string Resignation = "Resignation";
    public const string Other = "Other";

    public static readonly IReadOnlyList<string> All =
    [
        Leave, AttendanceCorrection, Expense, Travel, Letter, ProfileChange, ShiftSwap, Resignation, Other
    ];
}
