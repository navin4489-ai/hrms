using System.ComponentModel.DataAnnotations;
using Wintech.Hrms.Api.Domain.Cases;

namespace Wintech.Hrms.Api.Modules.Cases;

// ---------------------------------------------------------------- GET /cases/categories

public sealed record CaseSubcategoryDto(Guid Id, string Name);

public sealed record CaseCategoryDto(Guid Id, string Name, bool Confidential,
    IReadOnlyList<CaseSubcategoryDto> Subcategories);

// ---------------------------------------------------------------- GET /cases (list item)

public class CaseListItem
{
    public Guid Id { get; init; }
    public string TicketNumber { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string? Category { get; init; }
    public string? Subcategory { get; init; }
    public CasePriority Priority { get; init; }
    public CaseStatus Status { get; init; }
    public string Queue { get; init; } = string.Empty;
    public bool Confidential { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public DateTime SlaDueAt { get; init; }
    public DateTime? FirstResponseAt { get; init; }
    public DateTime? ResolvedAt { get; init; }
    public string? AssigneeName { get; init; }
    public string? EmployeeName { get; init; }
    public int? Rating { get; init; }
}

// ---------------------------------------------------------------- POST /cases

public sealed class CreateCaseRequest : IValidatableObject
{
    [Required(ErrorMessage = "A category is required.")]
    public Guid CategoryId { get; set; }

    public Guid? SubcategoryId { get; set; }

    [Required(ErrorMessage = "A subject is required.")]
    [StringLength(300, ErrorMessage = "The subject must be 300 characters or fewer.")]
    public string Subject { get; set; } = string.Empty;

    [Required(ErrorMessage = "A description is required.")]
    [StringLength(4000, ErrorMessage = "The description must be 4000 characters or fewer.")]
    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "An urgency is required.")]
    public CasePriority Urgency { get; set; } = CasePriority.Medium;

    [StringLength(500, ErrorMessage = "The attachment URL must be 500 characters or fewer.")]
    public string? AttachmentUrl { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Enum.IsDefined(Urgency))
            yield return new ValidationResult("Urgency must be one of Low, Medium, High or Urgent.",
                [nameof(Urgency)]);
    }
}

public sealed record CaseCreatedResult(Guid Id, string TicketNumber, CaseStatus Status, DateTime SlaDueAt);

// ---------------------------------------------------------------- GET /cases/{id}

public sealed class CaseMessageDto
{
    public Guid Id { get; init; }
    public string SenderName { get; init; } = string.Empty;
    public string SenderRole { get; init; } = string.Empty;
    public string Visibility { get; init; } = "Employee";
    public string Body { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public string? AttachmentUrl { get; init; }
}

public sealed class CaseDetailDto : CaseListItem
{
    public string Description { get; init; } = string.Empty;
    public string? ResolutionCode { get; init; }
    public DateTime? ClosedAt { get; init; }
    public int ReopenCount { get; init; }
    public string? RatingComment { get; init; }
    public bool CanReply { get; init; }
    public IReadOnlyList<CaseMessageDto> Messages { get; init; } = [];
}

// ---------------------------------------------------------------- POST /cases/{id}/messages | /notes

public sealed class AddCaseMessageRequest
{
    [Required(ErrorMessage = "A message body is required.")]
    [StringLength(4000, ErrorMessage = "The message must be 4000 characters or fewer.")]
    public string Body { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "The attachment URL must be 500 characters or fewer.")]
    public string? AttachmentUrl { get; set; }
}

public sealed class AddCaseNoteRequest
{
    [Required(ErrorMessage = "A note body is required.")]
    [StringLength(4000, ErrorMessage = "The note must be 4000 characters or fewer.")]
    public string Body { get; set; } = string.Empty;
}

// ---------------------------------------------------------------- POST /cases/{id}/transition | /rate

public sealed class CaseTransitionRequest
{
    [Required(ErrorMessage = "An action is required.")]
    public CaseTransitionAction Action { get; set; }

    [StringLength(1000, ErrorMessage = "The comment must be 1000 characters or fewer.")]
    public string? Comment { get; set; }

    [StringLength(100, ErrorMessage = "The resolution code must be 100 characters or fewer.")]
    public string? ResolutionCode { get; set; }
}

public sealed class RateCaseRequest : IValidatableObject
{
    [Range(1, 5, ErrorMessage = "Rating must be between 1 and 5.")]
    public int Rating { get; set; }

    [StringLength(1000, ErrorMessage = "The comment must be 1000 characters or fewer.")]
    public string? Comment { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Rating is < 1 or > 5)
            yield return new ValidationResult("Rating must be between 1 and 5.", [nameof(Rating)]);
    }
}

// ---------------------------------------------------------------- knowledge base

public sealed record KnowledgeArticleListItem(Guid Id, string Title, string Summary, string Category,
    IReadOnlyList<string> Tags, int HelpfulCount, DateTime UpdatedAt);

public sealed record KnowledgeArticleDetail(Guid Id, string Title, string Body, string Category,
    string Version, DateOnly? ExpiresAt);
