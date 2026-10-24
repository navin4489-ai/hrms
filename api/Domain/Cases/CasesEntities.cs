using Wintech.Hrms.Api.Domain.Employees;

namespace Wintech.Hrms.Api.Domain.Cases;

public enum CasePriority
{
    Low = 0,
    Medium = 1,
    High = 2,
    Urgent = 3
}

public enum CaseStatus
{
    Open = 0,
    WaitingOnEmployee = 1,
    WaitingOnHR = 2,
    Resolved = 3,
    Closed = 4,
    Reopened = 5
}

public enum CaseMessageVisibility
{
    /// <summary>Visible to the case's employee (FR-EMP-112/113).</summary>
    Employee = 0,
    /// <summary>HR-internal only; never shown to employees (FR-ADM-122).</summary>
    Internal = 1
}

public enum CaseTransitionAction
{
    Resolve = 0,
    Close = 1,
    Reopen = 2
}

/// <summary>Helpdesk service category with optional confidentiality flag (FR-EMP-111, FR-ADM-120/121).</summary>
public class CaseCategory
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Cases in this category live in the restricted queue (FR-EMP-114, UAT-009).</summary>
    public bool Confidential { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public ICollection<CaseSubcategory> Subcategories { get; set; } = [];
}

public class CaseSubcategory
{
    public Guid Id { get; set; }
    public Guid CategoryId { get; set; }
    public CaseCategory? Category { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

/// <summary>HR helpdesk ticket (FR-EMP-110..114).</summary>
public class HelpdeskCase
{
    public Guid Id { get; set; }
    public string TicketNumber { get; set; } = string.Empty;
    public Guid CategoryId { get; set; }
    public CaseCategory? Category { get; set; }
    public Guid? SubcategoryId { get; set; }
    public CaseSubcategory? Subcategory { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public CasePriority Priority { get; set; } = CasePriority.Medium;
    public CaseStatus Status { get; set; } = CaseStatus.Open;
    /// <summary>Assignment queue: HR, Payroll, IT, Facilities, General HR or Restricted (FR-ADM-121).</summary>
    public string Queue { get; set; } = "HR";
    /// <summary>Copied from the category; confidential cases are 404 outside the restricted queue.</summary>
    public bool Confidential { get; set; }
    /// <summary>The employee who raised the case.</summary>
    public Guid EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public Guid? AssigneeUserId { get; set; }
    public Guid? AssigneeEmployeeId { get; set; }
    public Employee? Assignee { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime SlaDueAt { get; set; }
    public DateTime? FirstResponseAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public DateTime? ReopenedAt { get; set; }
    public int ReopenCount { get; set; }
    public string? ResolutionCode { get; set; }
    /// <summary>Employee satisfaction 1-5, set once after resolution (FR-EMP-113).</summary>
    public int? Rating { get; set; }
    public string? RatingComment { get; set; }
    public ICollection<CaseMessage> Messages { get; set; } = [];
}

/// <summary>Employee-visible reply or HR-internal note (FR-ADM-122).</summary>
public class CaseMessage
{
    public Guid Id { get; set; }
    public Guid CaseId { get; set; }
    public HelpdeskCase? Case { get; set; }
    public Guid? SenderUserId { get; set; }
    public string SenderName { get; set; } = string.Empty;
    /// <summary>"Employee", "HR" or "System".</summary>
    public string SenderRole { get; set; } = "Employee";
    public CaseMessageVisibility Visibility { get; set; } = CaseMessageVisibility.Employee;
    public string Body { get; set; } = string.Empty;
    public string? AttachmentUrl { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Searchable FAQ / suggested article (FR-EMP-115, FR-ADM-125).</summary>
public class CaseKnowledgeArticle
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    /// <summary>Comma-separated tags for search.</summary>
    public string Tags { get; set; } = string.Empty;
    public int HelpfulCount { get; set; }
    public string Version { get; set; } = "1.0";
    public DateTime UpdatedAt { get; set; }
    /// <summary>Articles expire on review date (FR-ADM-125).</summary>
    public DateOnly? ExpiresOn { get; set; }
    public bool IsActive { get; set; } = true;
}
