using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Auditing;
using Wintech.Hrms.Api.Common.CurrentUser;
using Wintech.Hrms.Api.Common.ErrorHandling;
using Wintech.Hrms.Api.Common.Registration;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Performance;

namespace Wintech.Hrms.Api.Modules.Performance;

/// <summary>
/// Performance cycle self-service (FR-EMP-100..102, FR-ADM-110..112).
/// Employees see their own cycle, goals, self-assessment window and published reviews only.
/// </summary>
public interface IPerformanceService : IAppService
{
    Task<CurrentCycleResult?> GetCurrentCycleAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GoalItem>> ListGoalsAsync(Guid? cycleId, CancellationToken cancellationToken = default);
    Task<SelfAssessmentResult> SubmitSelfAssessmentAsync(SubmitSelfAssessmentForm form, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReviewItem>> ListReviewsAsync(CancellationToken cancellationToken = default);
    Task AcknowledgeReviewAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class PerformanceService : IPerformanceService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly IClock _clock;

    public PerformanceService(AppDbContext db, ICurrentUser currentUser, IAuditService audit, IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
    }

    public async Task<CurrentCycleResult?> GetCurrentCycleAsync(CancellationToken cancellationToken = default)
    {
        _currentUser.EnsureActive();
        var today = _clock.Today;

        var cycle = await _db.Set<PerformanceCycle>().AsNoTracking()
            .Where(c => c.Status == PerformanceCycleStatus.Open && c.StartDate <= today && c.EndDate >= today)
            .OrderByDescending(c => c.StartDate)
            .FirstOrDefaultAsync(cancellationToken);

        if (cycle is null) return null;

        var employeeId = _currentUser.EmployeeId;
        var submitted = employeeId is null
            ? null
            : await _db.Set<SelfAssessment>().AsNoTracking()
                .FirstOrDefaultAsync(s => s.CycleId == cycle.Id && s.EmployeeId == employeeId.Value, cancellationToken);

        return new CurrentCycleResult
        {
            Id = cycle.Id,
            Name = cycle.Name,
            StartDate = cycle.StartDate,
            EndDate = cycle.EndDate,
            Status = cycle.Status,
            SelfAssessment = new SelfAssessmentStatusResult
            {
                Status = submitted is null ? "NotStarted" : "Submitted",
                DueDate = cycle.SelfAssessmentDueDate,
                SubmittedAt = submitted?.SubmittedAt
            }
        };
    }

    public async Task<IReadOnlyList<GoalItem>> ListGoalsAsync(Guid? cycleId, CancellationToken cancellationToken = default)
    {
        _currentUser.EnsureActive();
        var employeeId = RequireEmployeeId();

        if (cycleId is { } id &&
            !await _db.Set<PerformanceCycle>().AsNoTracking().AnyAsync(c => c.Id == id, cancellationToken))
            throw new NotFoundException("The performance cycle was not found.");

        var goals = await _db.Set<PerformanceGoal>().AsNoTracking()
            .Where(g => g.EmployeeId == employeeId &&
                        (cycleId == null || g.CycleId == cycleId.Value))
            .OrderBy(g => g.Category).ThenBy(g => g.Title)
            .ToListAsync(cancellationToken);

        return goals.Select(g => new GoalItem
        {
            Id = g.Id,
            Title = g.Title,
            Description = g.Description,
            Category = g.Category,
            Weight = g.Weight,
            Target = g.Target,
            Progress = g.Progress,
            Status = g.Status,
            // Manager feedback is visible to the employee once published (FR-EMP-100).
            ManagerFeedback = g.FeedbackPublished ? g.ManagerFeedback : null
        }).ToList();
    }

    public async Task<SelfAssessmentResult> SubmitSelfAssessmentAsync(SubmitSelfAssessmentForm form,
        CancellationToken cancellationToken = default)
    {
        _currentUser.EnsureActive();
        var employeeId = RequireEmployeeId();
        var today = _clock.Today;

        var cycle = await _db.Set<PerformanceCycle>().FirstOrDefaultAsync(c => c.Id == form.CycleId, cancellationToken)
                    ?? throw new NotFoundException("The performance cycle was not found.");

        // FR-EMP-101: submissions only inside an open cycle.
        if (cycle.Status != PerformanceCycleStatus.Open || today > cycle.EndDate)
            throw new ConflictException("This review cycle is closed; self-assessments can no longer be submitted.");

        var alreadySubmitted = await _db.Set<SelfAssessment>().AsNoTracking()
            .AnyAsync(s => s.CycleId == cycle.Id && s.EmployeeId == employeeId, cancellationToken);
        if (alreadySubmitted)
            throw new ConflictException("You have already submitted a self-assessment for this cycle.");

        var assessment = new SelfAssessment
        {
            Id = Guid.NewGuid(),
            CycleId = cycle.Id,
            EmployeeId = employeeId,
            Content = form.Content.Trim(),
            Evidence = string.IsNullOrWhiteSpace(form.Evidence) ? null : form.Evidence.Trim(),
            SubmittedAt = _clock.UtcNow
        };
        _db.Set<SelfAssessment>().Add(assessment);

        _audit.Add("Performance.SelfAssessmentSubmitted", nameof(SelfAssessment), assessment.Id.ToString(),
            details: new { cycle.Id, cycle.Name, characters = assessment.Content.Length });
        await _db.SaveChangesAsync(cancellationToken);

        return new SelfAssessmentResult { Id = assessment.Id, Status = "Submitted" };
    }

    public async Task<IReadOnlyList<ReviewItem>> ListReviewsAsync(CancellationToken cancellationToken = default)
    {
        _currentUser.EnsureActive();
        var employeeId = RequireEmployeeId();

        var reviews = await (
            from r in _db.Set<PerformanceReview>().AsNoTracking()
            join c in _db.Set<PerformanceCycle>().AsNoTracking() on r.CycleId equals c.Id
            where r.EmployeeId == employeeId && r.IsPublished
            orderby (r.PublishedAt ?? DateTime.MinValue) descending
            select new { r.Id, CycleName = c.Name, r.PublishedAt, r.Rating, r.Summary, r.AcknowledgedAt })
            .ToListAsync(cancellationToken);

        return reviews.Select(r => new ReviewItem
        {
            Id = r.Id,
            CycleName = r.CycleName,
            PublishedAt = r.PublishedAt,
            Rating = r.Rating,
            Summary = r.Summary,
            AcknowledgedAt = r.AcknowledgedAt
        }).ToList();
    }

    public async Task AcknowledgeReviewAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _currentUser.EnsureActive();
        var employeeId = RequireEmployeeId();

        var review = await _db.Set<PerformanceReview>().FirstOrDefaultAsync(
            r => r.Id == id && r.EmployeeId == employeeId && r.IsPublished, cancellationToken)
            // Another employee's review is 404 — no existence leak.
            ?? throw new NotFoundException("The performance review was not found.");

        // Acknowledgement of receipt, not agreement (FR-EMP-102); idempotent.
        if (review.AcknowledgedAt is null)
        {
            review.AcknowledgedAt = _clock.UtcNow;
            _audit.Add("Performance.ReviewAcknowledged", nameof(PerformanceReview), review.Id.ToString());
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private Guid RequireEmployeeId() => _currentUser.EmployeeId
        ?? throw new BusinessRuleException("No employee record is linked to your account. Contact HR.");
}

/// <summary>Training catalogue and enrolments (FR-EMP-103/104, FR-ADM-113).</summary>
public interface ITrainingService : IAppService
{
    Task<IReadOnlyList<CourseItem>> ListCoursesAsync(TrainingCourseQuery query, CancellationToken cancellationToken = default);
    Task<EnrollmentResult> EnrollAsync(Guid courseId, CancellationToken cancellationToken = default);
    Task<CourseCompletionResult> CompleteAsync(Guid courseId, decimal? score, CancellationToken cancellationToken = default);
}

public sealed class TrainingService : ITrainingService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly IClock _clock;

    public TrainingService(AppDbContext db, ICurrentUser currentUser, IAuditService audit, IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
    }

    public async Task<IReadOnlyList<CourseItem>> ListCoursesAsync(TrainingCourseQuery query,
        CancellationToken cancellationToken = default)
    {
        _currentUser.EnsureActive();
        var employeeId = RequireEmployeeId();
        var today = _clock.Today;

        var courses = await _db.Set<TrainingCourse>().AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.Mandatory ? 0 : 1).ThenBy(c => c.Title)
            .ToListAsync(cancellationToken);

        var enrollments = await _db.Set<TrainingEnrollment>().AsNoTracking()
            .Where(e => e.EmployeeId == employeeId && courses.Select(c => c.Id).Contains(e.CourseId))
            .ToListAsync(cancellationToken);
        var enrollmentByCourse = enrollments.ToDictionary(e => e.CourseId);

        var items = courses.Select(course =>
        {
            enrollmentByCourse.TryGetValue(course.Id, out var enrollment);
            return new
            {
                Course = course,
                Enrollment = enrollment,
                DisplayStatus = DisplayStatus(course, enrollment, today)
            };
        });

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (query.Status.Trim().Equals("NotEnrolled", StringComparison.OrdinalIgnoreCase) ||
                query.Status.Trim().Equals("Enrolled", StringComparison.OrdinalIgnoreCase) ||
                query.Status.Trim().Equals("Completed", StringComparison.OrdinalIgnoreCase) ||
                query.Status.Trim().Equals("Overdue", StringComparison.OrdinalIgnoreCase))
            {
                items = items.Where(i => i.DisplayStatus.Equals(query.Status.Trim(), StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                throw new Common.ErrorHandling.RequestValidationException(errors: new()
                {
                    ["status"] = ["Expected NotEnrolled, Enrolled, Completed or Overdue."]
                });
            }
        }

        if (query.Mandatory is { } mandatory)
            items = items.Where(i => i.Course.Mandatory == mandatory);

        return items.Select(i => new CourseItem
        {
            Id = i.Course.Id,
            Title = i.Course.Title,
            Category = i.Course.Category,
            DurationHours = i.Course.DurationHours,
            Mandatory = i.Course.Mandatory,
            DueDate = i.Course.DueDate,
            EnrollmentStatus = i.DisplayStatus,
            CompletedAt = i.Enrollment?.CompletedAt,
            CertificateUrl = i.Enrollment is { CertificateStorageKey: not null }
                ? $"/api/v1/training/courses/{i.Course.Id}/certificate"
                : null
        }).ToList();
    }

    public async Task<EnrollmentResult> EnrollAsync(Guid courseId, CancellationToken cancellationToken = default)
    {
        _currentUser.EnsureActive();
        var employeeId = RequireEmployeeId();
        var today = _clock.Today;

        var course = await _db.Set<TrainingCourse>().AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == courseId, cancellationToken)
            ?? throw new NotFoundException("The course was not found.");

        if (!course.IsActive)
            throw new BusinessRuleException("This course is not currently open for enrolment.",
                new() { ["courseId"] = ["The course is inactive."] });

        var existing = await _db.Set<TrainingEnrollment>().AsNoTracking()
            .FirstOrDefaultAsync(e => e.CourseId == courseId && e.EmployeeId == employeeId, cancellationToken);
        if (existing is not null)
            throw new ConflictException(existing.Status == TrainingEnrollmentStatus.Completed
                ? "You have already completed this course."
                : "You are already enrolled in this course.");

        // FR-EMP-104: enrol only while the course is still eligible (mandatory past its due date is out).
        if (course.Mandatory && course.DueDate is { } due && due < today)
            throw new BusinessRuleException("This mandatory course is past its due date; contact training.admin.",
                new() { ["courseId"] = ["Enrolment window has closed."] });

        var enrollment = new TrainingEnrollment
        {
            Id = Guid.NewGuid(),
            CourseId = course.Id,
            EmployeeId = employeeId,
            Status = TrainingEnrollmentStatus.Enrolled,
            EnrolledAt = _clock.UtcNow
        };
        _db.Set<TrainingEnrollment>().Add(enrollment);

        _audit.Add("Training.Enrolled", nameof(TrainingEnrollment), enrollment.Id.ToString(),
            details: new { course.Id, course.Title });
        await _db.SaveChangesAsync(cancellationToken);

        return new EnrollmentResult
        {
            Id = enrollment.Id,
            CourseId = enrollment.CourseId,
            Status = "Enrolled",
            EnrolledAt = enrollment.EnrolledAt
        };
    }

    public async Task<CourseCompletionResult> CompleteAsync(Guid courseId, decimal? score,
        CancellationToken cancellationToken = default)
    {
        _currentUser.EnsureActive();
        var employeeId = RequireEmployeeId();

        var course = await _db.Set<TrainingCourse>().AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == courseId, cancellationToken)
            ?? throw new NotFoundException("The course was not found.");

        var enrollment = await _db.Set<TrainingEnrollment>().FirstOrDefaultAsync(
            e => e.CourseId == courseId && e.EmployeeId == employeeId, cancellationToken);
        if (enrollment is null)
            throw new ConflictException("You are not enrolled in this course.");
        if (enrollment.Status == TrainingEnrollmentStatus.Completed)
            throw new ConflictException("This course is already marked completed.");

        // FR-EMP-103: self-service completion applies to optional courses only.
        if (course.Mandatory && !_currentUser.HasPermission(Security.Permissions.TrainingManage))
            throw new ForbiddenException("Mandatory course completions must be recorded by training administration.");

        enrollment.Status = TrainingEnrollmentStatus.Completed;
        enrollment.CompletedAt = _clock.UtcNow;
        enrollment.Score = score;

        _audit.Add("Training.Completed", nameof(TrainingEnrollment), enrollment.Id.ToString(),
            details: new { course.Title, score });
        await _db.SaveChangesAsync(cancellationToken);

        return new CourseCompletionResult { CompletedAt = enrollment.CompletedAt };
    }

    private Guid RequireEmployeeId() => _currentUser.EmployeeId
        ?? throw new BusinessRuleException("No employee record is linked to your account. Contact HR.");

    /// <summary>NotEnrolled | Enrolled | Completed | Overdue (mandatory past due dates).</summary>
    private static string DisplayStatus(TrainingCourse course, TrainingEnrollment? enrollment, DateOnly today)
    {
        if (enrollment is null)
            return course.Mandatory && course.DueDate is { } due && due < today ? "Overdue" : "NotEnrolled";

        if (enrollment.Status == TrainingEnrollmentStatus.Completed) return "Completed";

        return course.Mandatory && course.DueDate is { } mandatoryDue && mandatoryDue < today
            ? "Overdue"
            : "Enrolled";
    }
}
