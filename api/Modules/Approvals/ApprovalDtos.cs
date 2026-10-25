using System.ComponentModel.DataAnnotations;
using Wintech.Hrms.Api.Domain.Approvals;

namespace Wintech.Hrms.Api.Modules.Approvals;

/// <summary>GET /approvals/pending item (contract section 9).</summary>
public sealed record PendingApprovalItem(
    Guid TaskId,
    string RequestType,
    Guid RequestId,
    string Title,
    string RequesterName,
    string? RequesterEmployeeNumber,
    DateTime RequestedAt,
    string StepName,
    DateTime? DueAt,
    decimal? Amount,
    string? Currency,
    string Status,
    string DeepLink);

/// <summary>GET /approvals/mine item (contract section 9).</summary>
public sealed record MyApprovalItem(
    Guid TaskId,
    string RequestType,
    Guid RequestId,
    string Title,
    string Status,
    string CurrentStep,
    string? ApproverName,
    DateTime UpdatedAt);

/// <summary>GET /approvals/stats payload.</summary>
public sealed record ApprovalStatsDto(int Pending, int Overdue, int CompletedThisMonth);

/// <summary>One entry of the task history shown by GET /approvals/{taskId}.</summary>
public sealed record ApprovalHistoryItem(
    int Step,
    ApprovalActionType Action,
    string ActorName,
    string? Comment,
    DateTime At);

/// <summary>GET /approvals/{taskId} payload.</summary>
public sealed class ApprovalDetailDto
{
    public Guid TaskId { get; init; }
    public string RequestType { get; init; } = string.Empty;
    public Guid RequestId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string CurrentStep { get; init; } = string.Empty;
    public DateTime? DueAt { get; init; }
    public string? RequesterName { get; init; }
    public bool CanDecide { get; init; }
    public IReadOnlyList<ApprovalHistoryItem> History { get; init; } = [];
}

/// <summary>POST /approvals/{taskId}/decision body. Action: Approve | Reject | Return.</summary>
public sealed class ApprovalDecisionRequest
{
    [Required]
    public string Action { get; set; } = string.Empty;

    public string? Comment { get; set; }
}

/// <summary>POST /approvals/{taskId}/decision response.</summary>
public sealed record ApprovalDecisionResult(
    Guid TaskId,
    string Status,
    bool Completed,
    string Message);

/// <summary>POST /approvals/{taskId}/delegate body (FR-MGR-003).</summary>
public sealed class ApprovalDelegateRequest
{
    public Guid ToUserId { get; set; }

    public string? Reason { get; set; }
}

/// <summary>GET /workflows item (admin.config).</summary>
public sealed record WorkflowDto(
    Guid Id,
    string Code,
    string Name,
    int Version,
    bool IsActive,
    IReadOnlyList<WorkflowStepDto> Steps);

/// <summary>One step of a workflow definition.</summary>
public sealed record WorkflowStepDto(
    int StepNumber,
    string Name,
    ApproverType ApproverType,
    string? ApproverValue,
    int? SlaDays);

public sealed class UpsertWorkflowVersionRequest
{
    [Required, StringLength(200)]
    public string Name { get; init; } = string.Empty;

    [MinLength(1), MaxLength(10)]
    public List<UpsertWorkflowStepRequest> Steps { get; init; } = [];
}

public sealed class UpsertWorkflowStepRequest
{
    [Required, StringLength(200)]
    public string Name { get; init; } = string.Empty;

    [Required]
    public ApproverType? ApproverType { get; init; }

    [StringLength(200)]
    public string? ApproverValue { get; init; }

    [Range(1, 365)]
    public int? SlaDays { get; init; }
}
