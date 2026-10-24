namespace Wintech.Hrms.Api.Domain.Performance;

public enum PerformanceCycleStatus
{
    Upcoming = 0,
    Open = 1,
    Closed = 2
}

public enum GoalStatus
{
    NotStarted = 0,
    OnTrack = 1,
    AtRisk = 2,
    Completed = 3,
    Dropped = 4
}

public enum TrainingEnrollmentStatus
{
    Enrolled = 0,
    Completed = 1
}

/// <summary>Review cycle (FR-ADM-110). Self-assessment window rides on the cycle.</summary>
public class PerformanceCycle
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public PerformanceCycleStatus Status { get; set; } = PerformanceCycleStatus.Open;
    public DateOnly? SelfAssessmentDueDate { get; set; }
}

/// <summary>Goal assigned to an employee for a cycle (FR-EMP-100).</summary>
public class PerformanceGoal
{
    public Guid Id { get; set; }
    public Guid CycleId { get; set; }
    public Guid EmployeeId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Category { get; set; }
    public int Weight { get; set; }
    public string? Target { get; set; }
    /// <summary>0-100.</summary>
    public int Progress { get; set; }
    public GoalStatus Status { get; set; } = GoalStatus.NotStarted;
    /// <summary>Visible to the employee only once published by the manager.</summary>
    public bool FeedbackPublished { get; set; }
    public string? ManagerFeedback { get; set; }
}

/// <summary>Employee self-assessment, one per cycle (FR-EMP-101).</summary>
public class SelfAssessment
{
    public Guid Id { get; set; }
    public Guid CycleId { get; set; }
    public Guid EmployeeId { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? Evidence { get; set; }
    public DateTime SubmittedAt { get; set; }
}

/// <summary>Published performance review (FR-EMP-102). Acknowledgement != agreement.</summary>
public class PerformanceReview
{
    public Guid Id { get; set; }
    public Guid CycleId { get; set; }
    public Guid EmployeeId { get; set; }
    public bool IsPublished { get; set; }
    public DateTime? PublishedAt { get; set; }
    public decimal? Rating { get; set; }
    public string? Summary { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
}

/// <summary>Training catalogue entry (FR-ADM-113, FR-EMP-103).</summary>
public class TrainingCourse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Category { get; set; }
    public decimal DurationHours { get; set; }
    public bool Mandatory { get; set; }
    public DateOnly? DueDate { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Course enrolment (FR-EMP-104). Display status Overdue is computed for mandatory courses.</summary>
public class TrainingEnrollment
{
    public Guid Id { get; set; }
    public Guid CourseId { get; set; }
    public Guid EmployeeId { get; set; }
    public TrainingEnrollmentStatus Status { get; set; } = TrainingEnrollmentStatus.Enrolled;
    public DateTime EnrolledAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public decimal? Score { get; set; }
    public string? CertificateStorageKey { get; set; }
}
