namespace Wintech.Hrms.Api.Domain.Lifecycle;

// ---------------------------------------------------------------- enums (serialized as strings)

/// <summary>Resignation workflow state (contract §21).</summary>
public enum ResignationStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Cancelled = 3,
    Withdrawn = 4
}

public enum ResignationReasonCategory
{
    BetterOpportunity = 0,
    HigherEducation = 1,
    Relocation = 2,
    Personal = 3,
    Health = 4,
    Family = 5,
    Other = 6
}

/// <summary>The six exit checklist tasks created when a resignation is approved (FR-EMP-131/132).</summary>
public enum ExitTaskKind
{
    KnowledgeHandover = 0,
    AssetReturn = 1,
    AccessRevocation = 2,
    ExitSurvey = 3,
    DocumentAcknowledgement = 4,
    FullAndFinal = 5
}

/// <summary>Exit checklist ownership — contract §21: Employee|Manager|HR|IT|Payroll.</summary>
public enum ExitTaskOwner
{
    Employee = 0,
    Manager = 1,
    HR = 2,
    IT = 3,
    Payroll = 4
}

/// <summary>Exit checklist item status — contract §21: NotStarted|InProgress|Completed|Waived.</summary>
public enum ExitChecklistStatus
{
    NotStarted = 0,
    InProgress = 1,
    Completed = 2,
    Waived = 3
}

public enum LifecycleChecklistType
{
    Onboarding = 0,
    Offboarding = 1
}

public enum LifecycleStage
{
    PreBoarding = 0,
    Day1 = 1,
    Week1 = 2,
    Completed = 3,
    Exit = 4
}

/// <summary>Checklist task ownership — contract §22: Employee|Manager|HR|IT|Facilities|Payroll.</summary>
public enum LifecycleTaskOwner
{
    Employee = 0,
    Manager = 1,
    HR = 2,
    IT = 3,
    Facilities = 4,
    Payroll = 5
}

/// <summary>Checklist task status — contract §22: NotStarted|InProgress|Completed|Waived|Overdue.</summary>
public enum LifecycleTaskStatus
{
    NotStarted = 0,
    InProgress = 1,
    Completed = 2,
    Waived = 3,
    /// <summary>Derived at read time (NotStarted/InProgress past its due date); never stored.</summary>
    Overdue = 4
}

public enum LifecycleTaskAction
{
    Start = 0,
    Complete = 1,
    Reopen = 2,
    Waive = 3
}

// ---------------------------------------------------------------- resignation & exit

/// <summary>One resignation per employee (FR-EMP-130). Table: lc_resignations.</summary>
public class Resignation
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public ResignationStatus Status { get; set; } = ResignationStatus.Pending;
    public DateTime SubmittedAt { get; set; }
    public DateOnly LastWorkingDay { get; set; }
    public ResignationReasonCategory ReasonCategory { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Comments { get; set; }
    /// <summary>Notice policy snapshot taken from lc_config key Resignation.NoticeDays.</summary>
    public int NoticePeriodDays { get; set; }
    /// <summary>Task id returned by IApprovalService.StartAsync (null while the engine is unavailable).</summary>
    public Guid? ApprovalTaskId { get; set; }
    /// <summary>Set when approved — equals LastWorkingDay.</summary>
    public DateOnly? RelievingDate { get; set; }
    public DateTime? DecidedAt { get; set; }
    public DateTime? WithdrawnAt { get; set; }
    public string? WithdrawReason { get; set; }
}

/// <summary>Decision history row rendered as { step, action, actorName, comment, at } (contract §21).</summary>
public class ResignationEvent
{
    public Guid Id { get; set; }
    public Guid ResignationId { get; set; }
    public string Step { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string ActorName { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public DateTime At { get; set; }
}

/// <summary>One exit checklist item (FR-EMP-131/132). Table: lc_exit_checklist_items.</summary>
public class ExitChecklistItem
{
    public Guid Id { get; set; }
    public Guid ResignationId { get; set; }
    public ExitTaskKind Task { get; set; }
    public ExitTaskOwner Owner { get; set; }
    public ExitChecklistStatus Status { get; set; } = ExitChecklistStatus.NotStarted;
    public DateOnly DueDate { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Guid? CompletedByUserId { get; set; }
    public string? Note { get; set; }
    public string? AttachmentUrl { get; set; }
}

/// <summary>Employee exit survey (FR-EMP-132). One per resignation. Table: lc_exit_surveys.</summary>
public class ExitSurvey
{
    public Guid Id { get; set; }
    public Guid ResignationId { get; set; }
    public int OverallRating { get; set; }
    public int RecommendScore { get; set; }
    public string? Likes { get; set; }
    public string? Improvements { get; set; }
    public bool WouldRejoin { get; set; }
    public Guid SubmittedByUserId { get; set; }
    public DateTime SubmittedAt { get; set; }
}

// ---------------------------------------------------------------- onboarding / offboarding checklists

/// <summary>Checklist materialised per employee (FR-ADM-025/031..035). Table: lc_checklists.</summary>
public class LifecycleChecklist
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public LifecycleChecklistType Type { get; set; }
    public string TemplateCode { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly? DueDate { get; set; }
    /// <summary>"Active" | "Completed" — recomputed whenever a task transitions.</summary>
    public string Status { get; set; } = "Active";
    public DateTime CreatedAt { get; set; }
}

/// <summary>One task inside a materialised checklist. Table: lc_checklist_tasks.</summary>
public class LifecycleChecklistTask
{
    public Guid Id { get; set; }
    public Guid ChecklistId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public LifecycleTaskOwner Owner { get; set; }
    public bool Mandatory { get; set; } = true;
    public LifecycleTaskStatus Status { get; set; } = LifecycleTaskStatus.NotStarted;
    public DateOnly? DueDate { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? CompletedBy { get; set; }
    public string? Note { get; set; }
    public int Order { get; set; }
}

/// <summary>Checklist audit trail row rendered as { action, actorName, at, note } (contract §22).</summary>
public class LifecycleChecklistEvent
{
    public Guid Id { get; set; }
    public Guid ChecklistId { get; set; }
    public Guid? TaskId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string ActorName { get; set; } = string.Empty;
    public DateTime At { get; set; }
    public string? Note { get; set; }
}

/// <summary>Default/custom checklist template (FR-ADM-025/031). Table: lc_templates.</summary>
public class LifecycleTemplate
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public LifecycleChecklistType Type { get; set; }
    public bool Active { get; set; } = true;
    public ICollection<LifecycleTemplateTask> Tasks { get; set; } = [];
}

/// <summary>Template task definition with a due-date offset relative to the checklist start date. Table: lc_template_tasks.</summary>
public class LifecycleTemplateTask
{
    public Guid Id { get; set; }
    public Guid TemplateId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public LifecycleTaskOwner Owner { get; set; }
    public bool Mandatory { get; set; } = true;
    public int DueOffsetDays { get; set; }
    public int Order { get; set; }
}

/// <summary>
/// Module-owned configuration row (table lc_config). The Lifecycle module does NOT read
/// Domain.Admin configuration (owned by another team): keys are looked up here with
/// code-level defaults when the row is missing (BR-012: changes apply prospectively).
/// </summary>
public class LifecycleSetting
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Description { get; set; }
}

/// <summary>
/// Canonical exit checklist definition used when a resignation is approved and by the seeder:
/// task, owner and due-date offset in days relative to the last working day.
/// </summary>
public static class LifecycleExitDefaults
{
    public static IReadOnlyList<(ExitTaskKind Task, ExitTaskOwner Owner, int DueOffsetDays)> Tasks { get; } =
    [
        (ExitTaskKind.KnowledgeHandover, ExitTaskOwner.Manager, -7),
        (ExitTaskKind.AssetReturn, ExitTaskOwner.IT, 0),
        (ExitTaskKind.AccessRevocation, ExitTaskOwner.IT, 0),
        (ExitTaskKind.ExitSurvey, ExitTaskOwner.Employee, -3),
        (ExitTaskKind.DocumentAcknowledgement, ExitTaskOwner.HR, -1),
        (ExitTaskKind.FullAndFinal, ExitTaskOwner.Payroll, 3)
    ];
}
