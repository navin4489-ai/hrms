using System.ComponentModel.DataAnnotations;
using Wintech.Hrms.Api.Domain.Lifecycle;

namespace Wintech.Hrms.Api.Modules.Lifecycle;

// ---------------------------------------------------------------- resignations (contract §21)

public sealed class CreateResignationRequest
{
    [Required(ErrorMessage = "A last working day is required.")]
    public DateOnly? LastWorkingDay { get; init; }

    [Required(ErrorMessage = "A reason category is required.")]
    public ResignationReasonCategory? ReasonCategory { get; init; }

    [Required(ErrorMessage = "A reason is required.")]
    [StringLength(1000)]
    public string Reason { get; init; } = string.Empty;

    [StringLength(2000)]
    public string? Comments { get; init; }
}

/// <summary>201 body: { id, status, approvalTaskId?, submittedAt, noticePeriodDays }.</summary>
public sealed class ResignationCreateResponse
{
    public Guid Id { get; init; }
    public ResignationStatus Status { get; init; }
    public Guid? ApprovalTaskId { get; init; }
    public DateTime SubmittedAt { get; init; }
    public int NoticePeriodDays { get; init; }
}

public sealed class ChecklistProgressDto
{
    public int Total { get; init; }
    public int Completed { get; init; }
    public int Overdue { get; init; }
}

public class ResignationListItem
{
    public Guid Id { get; init; }
    public ResignationStatus Status { get; init; }
    public DateTime SubmittedAt { get; init; }
    public DateOnly LastWorkingDay { get; init; }
    public string LastWorkingDayLabel { get; init; } = string.Empty;
    public ResignationReasonCategory ReasonCategory { get; init; }
    public string Reason { get; init; } = string.Empty;
    public Guid? ApprovalTaskId { get; init; }
    public string? CurrentApproverName { get; init; }
    public DateOnly? RelievingDate { get; init; }
    public ChecklistProgressDto ChecklistProgress { get; init; } = new();
}

public sealed class DecisionHistoryItemDto
{
    public string Step { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string ActorName { get; init; } = string.Empty;
    public string? Comment { get; init; }
    public DateTime At { get; init; }
}

public sealed class ExitChecklistItemDto
{
    public Guid Id { get; init; }
    public ExitTaskKind Task { get; init; }
    public ExitTaskOwner Owner { get; init; }
    public ExitChecklistStatus Status { get; init; }
    public DateOnly DueDate { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? Note { get; init; }
    public string? AttachmentUrl { get; init; }
}

public sealed class ResignationDetailDto : ResignationListItem
{
    public string? Comments { get; init; }
    public IReadOnlyList<DecisionHistoryItemDto> DecisionHistory { get; init; } = [];
    public IReadOnlyList<ExitChecklistItemDto> ExitChecklist { get; init; } = [];
}

public sealed class WithdrawRequest
{
    [Required(ErrorMessage = "A withdrawal reason is required.")]
    [StringLength(1000)]
    public string Reason { get; init; } = string.Empty;
}

public sealed class WithdrawResponse
{
    public ResignationStatus Status { get; init; }
}

public sealed class CompleteExitItemRequest
{
    [StringLength(1000)]
    public string? Note { get; init; }

    [StringLength(500)]
    public string? AttachmentUrl { get; init; }
}

public sealed class ExitSurveyRequest
{
    [Range(1, 5, ErrorMessage = "Overall rating must be between 1 and 5.")]
    public int OverallRating { get; init; }

    [Range(1, 10, ErrorMessage = "Recommend score must be between 1 and 10.")]
    public int RecommendScore { get; init; }

    [StringLength(2000)]
    public string? Likes { get; init; }

    [StringLength(2000)]
    public string? Improvements { get; init; }

    public bool WouldRejoin { get; init; }
}

public sealed class ExitSurveyDto
{
    public Guid Id { get; init; }
    public Guid ResignationId { get; init; }
    public int OverallRating { get; init; }
    public int RecommendScore { get; init; }
    public string? Likes { get; init; }
    public string? Improvements { get; init; }
    public bool WouldRejoin { get; init; }
    public DateTime SubmittedAt { get; init; }
}

// ---------------------------------------------------------------- lifecycle checklists (contract §22)

public class ChecklistListItem
{
    public Guid Id { get; init; }
    public LifecycleChecklistType Type { get; init; }
    public Guid EmployeeId { get; init; }
    public string EmployeeNumber { get; init; } = string.Empty;
    public string EmployeeName { get; init; } = string.Empty;
    public string? Designation { get; init; }
    public string? ManagerName { get; init; }
    public LifecycleStage Stage { get; init; }
    public int ProgressPercent { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly? DueDate { get; init; }
    public int OverdueCount { get; init; }
    public int TasksTotal { get; init; }
    public int TasksCompleted { get; init; }
}

public sealed class ChecklistTaskDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public LifecycleTaskOwner Owner { get; init; }
    public bool Mandatory { get; init; }
    public LifecycleTaskStatus Status { get; init; }
    public DateOnly? DueDate { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? CompletedBy { get; init; }
    public string? Note { get; init; }
    public int Order { get; init; }
}

public sealed class ChecklistAuditItemDto
{
    public string Action { get; init; } = string.Empty;
    public string ActorName { get; init; } = string.Empty;
    public DateTime At { get; init; }
    public string? Note { get; init; }
}

public sealed class ChecklistDetailDto : ChecklistListItem
{
    public IReadOnlyList<ChecklistTaskDto> Tasks { get; init; } = [];
    public IReadOnlyList<ChecklistAuditItemDto> AuditTrail { get; init; } = [];
}

public sealed class CreateChecklistRequest
{
    [Required(ErrorMessage = "An employee id is required.")]
    public Guid? EmployeeId { get; init; }

    [Required(ErrorMessage = "A checklist type is required.")]
    public LifecycleChecklistType? Type { get; init; }

    [StringLength(50)]
    public string? TemplateCode { get; init; }

    public DateOnly? StartDate { get; init; }
}

public sealed class TransitionTaskRequest
{
    [Required(ErrorMessage = "An action is required.")]
    public LifecycleTaskAction? Action { get; init; }

    [StringLength(1000)]
    public string? Note { get; init; }
}

public sealed class LifecycleTemplateTaskDto
{
    public string Code { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public LifecycleTaskOwner Owner { get; init; }
    public bool Mandatory { get; init; }
    public int DueOffsetDays { get; init; }
}

public sealed class LifecycleTemplateDto
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public LifecycleChecklistType Type { get; init; }
    public bool Active { get; init; }
    public int TaskCount { get; init; }
    public IReadOnlyList<LifecycleTemplateTaskDto> Tasks { get; init; } = [];
}
