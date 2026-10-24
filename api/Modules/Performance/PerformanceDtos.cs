using System.ComponentModel.DataAnnotations;
using Wintech.Hrms.Api.Domain.Performance;

namespace Wintech.Hrms.Api.Modules.Performance;

// ------------------------------------------------------------------ request forms

public sealed class SubmitSelfAssessmentForm
{
    [Required]
    public Guid CycleId { get; set; }

    [Required, StringLength(8000)]
    public string Content { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Evidence { get; set; }
}

public sealed class CompleteCourseForm
{
    [Range(0, 100)]
    public decimal? Score { get; set; }
}

public sealed class TrainingCourseQuery
{
    /// <summary>Enrolment status filter: NotEnrolled | Enrolled | Completed | Overdue.</summary>
    public string? Status { get; set; }
    public bool? Mandatory { get; set; }
}

// ------------------------------------------------------------------ responses

public sealed class CurrentCycleResult
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public PerformanceCycleStatus Status { get; init; }
    public SelfAssessmentStatusResult SelfAssessment { get; init; } = new();
}

public sealed class SelfAssessmentStatusResult
{
    /// <summary>NotStarted | Submitted (contract section 14).</summary>
    public string Status { get; init; } = "NotStarted";
    public DateOnly? DueDate { get; init; }
    public DateTime? SubmittedAt { get; init; }
}

public sealed class GoalItem
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Category { get; init; }
    public int Weight { get; init; }
    public string? Target { get; init; }
    public int Progress { get; init; }
    public GoalStatus Status { get; init; }
    public string? ManagerFeedback { get; init; }
}

public sealed class SelfAssessmentResult
{
    public Guid Id { get; init; }
    public string Status { get; init; } = "Submitted";
}

public sealed class ReviewItem
{
    public Guid Id { get; init; }
    public string CycleName { get; init; } = string.Empty;
    public DateTime? PublishedAt { get; init; }
    public decimal? Rating { get; init; }
    public string? Summary { get; init; }
    public DateTime? AcknowledgedAt { get; init; }
}

public sealed class CourseItem
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Category { get; init; }
    public decimal DurationHours { get; init; }
    public bool Mandatory { get; init; }
    public DateOnly? DueDate { get; init; }
    /// <summary>NotEnrolled | Enrolled | Completed | Overdue.</summary>
    public string EnrollmentStatus { get; init; } = "NotEnrolled";
    public DateTime? CompletedAt { get; init; }
    public string? CertificateUrl { get; init; }
}

public sealed class EnrollmentResult
{
    public Guid Id { get; init; }
    public Guid CourseId { get; init; }
    public string Status { get; init; } = "Enrolled";
    public DateTime EnrolledAt { get; init; }
}

public sealed class CourseCompletionResult
{
    public DateTime? CompletedAt { get; init; }
}
