using System.ComponentModel.DataAnnotations;
using Wintech.Hrms.Api.Domain.Announcements;

namespace Wintech.Hrms.Api.Modules.Announcements;

public sealed class AnnouncementDto
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public AnnouncementCategory Category { get; init; }
    public AnnouncementAudience Audience { get; init; }
    public bool Pinned { get; init; }
    public AnnouncementStatus Status { get; init; }
    public DateTime? PublishAt { get; init; }
    public DateTime? PublishedAt { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public bool RequiresAcknowledgement { get; init; }
    public DateTime? AcknowledgedAt { get; init; }
}

public sealed class CreateAnnouncementRequest : IValidatableObject
{
    [Required(ErrorMessage = "A title is required.")]
    [StringLength(200, ErrorMessage = "The title must be 200 characters or fewer.")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "A summary is required.")]
    [StringLength(500, ErrorMessage = "The summary must be 500 characters or fewer.")]
    public string Summary { get; set; } = string.Empty;

    [Required(ErrorMessage = "A body is required.")]
    [StringLength(8000, ErrorMessage = "The body must be 8000 characters or fewer.")]
    public string Body { get; set; } = string.Empty;

    [Required(ErrorMessage = "A category is required.")]
    public AnnouncementCategory Category { get; set; }

    [Required(ErrorMessage = "An audience is required.")]
    public AnnouncementAudience Audience { get; set; } = AnnouncementAudience.All;

    public List<Guid>? AudienceIds { get; set; }

    public bool RequiresAcknowledgement { get; set; }

    public bool Pinned { get; set; }

    public DateTime? PublishAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Enum.IsDefined(Category))
            yield return new ValidationResult("Category must be one of Company, HR, Policy, Event, IT or Wellness.",
                [nameof(Category)]);
        if (!Enum.IsDefined(Audience))
            yield return new ValidationResult("Audience must be one of All, Department or Location.", [nameof(Audience)]);
        if (Audience != AnnouncementAudience.All && (AudienceIds is null || AudienceIds.Count == 0))
            yield return new ValidationResult("Audience ids are required when the audience is a department or location.",
                [nameof(AudienceIds)]);
        if (ExpiresAt is { } expires && PublishAt is { } publish && expires <= publish)
            yield return new ValidationResult("The expiry must be after the publish time.", [nameof(ExpiresAt)]);
    }
}
