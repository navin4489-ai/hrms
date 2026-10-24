using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Registration;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Performance;

namespace Wintech.Hrms.Api.Data.Seed;

/// <summary>
/// Demo performance cycle (open, with goals for the demo employees, one published review awaiting
/// acknowledgement) and training catalogue with mixed enrolment states (Order 132).
/// Idempotent: skipped once any cycle or course exists.
/// </summary>
public sealed class PerformanceSeedData : IDataSeeder
{
    private readonly AppDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<PerformanceSeedData> _logger;

    public PerformanceSeedData(AppDbContext db, IClock clock, ILogger<PerformanceSeedData> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public int Order => 132;

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await _db.Set<PerformanceCycle>().AsNoTracking().AnyAsync(cancellationToken) ||
            await _db.Set<TrainingCourse>().AsNoTracking().AnyAsync(cancellationToken))
        {
            _logger.LogDebug("Performance/training seed data already present; skipping.");
            return;
        }

        var employees = await _db.Employees.AsNoTracking()
            .Where(e => e.EmployeeNumber == "EMP006" || e.EmployeeNumber == "EMP007" ||
                        e.EmployeeNumber == "EMP002")
            .ToDictionaryAsync(e => e.EmployeeNumber, e => e.Id, cancellationToken);
        var priya = employees["EMP006"];
        var rohit = employees["EMP007"];
        var anil = employees["EMP002"];

        // ---- cycles ---------------------------------------------------------
        var openCycle = new PerformanceCycle
        {
            Id = Guid.Parse("ff010000-0000-0000-0000-000000000001"),
            Name = "FY2026 H2 Review Cycle",
            StartDate = new DateOnly(2026, 7, 1),
            EndDate = new DateOnly(2026, 12, 31),
            Status = PerformanceCycleStatus.Open,
            SelfAssessmentDueDate = new DateOnly(2026, 11, 30)
        };
        var closedCycle = new PerformanceCycle
        {
            Id = Guid.Parse("ff010000-0000-0000-0000-000000000002"),
            Name = "FY2026 H1 Review Cycle",
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 6, 30),
            Status = PerformanceCycleStatus.Closed,
            SelfAssessmentDueDate = new DateOnly(2026, 6, 15)
        };
        _db.Set<PerformanceCycle>().AddRange(openCycle, closedCycle);

        // ---- goals ----------------------------------------------------------
        var goals = new List<PerformanceGoal>
        {
            new()
            {
                Id = Guid.Parse("ff010000-0000-0000-0000-000000000101"),
                CycleId = openCycle.Id, EmployeeId = priya,
                Title = "Ship payments v2 to production",
                Description = "Lead the rollout of the payments v2 integration for all merchants.",
                Category = "Delivery", Weight = 40,
                Target = "v2 live for 100% of merchants by December",
                Progress = 60, Status = GoalStatus.OnTrack,
                FeedbackPublished = true,
                ManagerFeedback = "Strong progress so far — keep an eye on the migration edge cases."
            },
            new()
            {
                Id = Guid.Parse("ff010000-0000-0000-0000-000000000102"),
                CycleId = openCycle.Id, EmployeeId = priya,
                Title = "Reduce API p95 latency",
                Description = "Bring the merchant API p95 latency under target.",
                Category = "Quality", Weight = 30,
                Target = "p95 <= 250 ms",
                Progress = 45, Status = GoalStatus.AtRisk,
                FeedbackPublished = true,
                ManagerFeedback = "Blocked on the caching work — escalate if the platform team slips."
            },
            new()
            {
                Id = Guid.Parse("ff010000-0000-0000-0000-000000000103"),
                CycleId = openCycle.Id, EmployeeId = priya,
                Title = "Mentor two junior engineers",
                Category = "People", Weight = 30,
                Target = "Two mentees clear their probation checkpoints",
                Progress = 80, Status = GoalStatus.OnTrack,
                // Not yet published — the employee must not see this feedback (FR-ADM-111).
                FeedbackPublished = false,
                ManagerFeedback = "Mentoring is going well; consider presenting the pairing playbook at the next guild."
            },
            new()
            {
                Id = Guid.Parse("ff010000-0000-0000-0000-000000000104"),
                CycleId = openCycle.Id, EmployeeId = rohit,
                Title = "Complete the engineering onboarding programme",
                Category = "Onboarding", Weight = 50,
                Target = "All onboarding milestones cleared in H2",
                Progress = 90, Status = GoalStatus.OnTrack,
                FeedbackPublished = true,
                ManagerFeedback = "Nearly there — finish the security training to close this out."
            },
            new()
            {
                Id = Guid.Parse("ff010000-0000-0000-0000-000000000105"),
                CycleId = openCycle.Id, EmployeeId = rohit,
                Title = "Raise unit test coverage on the billing module",
                Category = "Quality", Weight = 50,
                Target = "80% line coverage",
                Progress = 30, Status = GoalStatus.AtRisk,
                FeedbackPublished = true,
                ManagerFeedback = "Coverage is lagging; pair with Priya on the test harness."
            },
            new()
            {
                Id = Guid.Parse("ff010000-0000-0000-0000-000000000106"),
                CycleId = openCycle.Id, EmployeeId = anil,
                Title = "Engineering department OKRs",
                Description = "Deliver the department-level objectives for H2.",
                Category = "Leadership", Weight = 100,
                Target = "All H2 departmental key results green",
                Progress = 55, Status = GoalStatus.OnTrack,
                FeedbackPublished = true,
                ManagerFeedback = "On track; hiring pipeline needs attention."
            }
        };
        _db.Set<PerformanceGoal>().AddRange(goals);

        // ---- self-assessments (Rohit already submitted; Priya's is open for the demo flow) ----
        _db.Set<SelfAssessment>().Add(new SelfAssessment
        {
            Id = Guid.Parse("ff010000-0000-0000-0000-000000000201"),
            CycleId = openCycle.Id,
            EmployeeId = rohit,
            Content = "First half of H2: delivered the onboarding milestones, ramping up on billing. " +
                      "Biggest learning: the release drill process. Focus for the rest of the cycle: test coverage.",
            Evidence = "Onboarding checklist sign-off, release drill retrospective",
            SubmittedAt = new DateTime(2026, 10, 5, 14, 30, 0, DateTimeKind.Utc)
        });

        // ---- published reviews for the closed H1 cycle ------------------------
        _db.Set<PerformanceReview>().AddRange(
            new PerformanceReview
            {
                Id = Guid.Parse("ff010000-0000-0000-0000-000000000301"),
                CycleId = closedCycle.Id,
                EmployeeId = priya,
                IsPublished = true,
                PublishedAt = new DateTime(2026, 7, 10, 10, 0, 0, DateTimeKind.Utc),
                Rating = 4.2m,
                Summary = "Excellent delivery on the merchant platform; stepped up as the go-to engineer for payments."
            },
            new PerformanceReview
            {
                Id = Guid.Parse("ff010000-0000-0000-0000-000000000302"),
                CycleId = closedCycle.Id,
                EmployeeId = rohit,
                IsPublished = true,
                PublishedAt = new DateTime(2026, 7, 10, 10, 0, 0, DateTimeKind.Utc),
                Rating = 3.6m,
                Summary = "Solid ramp-up after joining; onboarding deliverables completed on schedule.",
                AcknowledgedAt = new DateTime(2026, 7, 12, 9, 15, 0, DateTimeKind.Utc)
            },
            new PerformanceReview
            {
                // Draft review for the open cycle — must never appear in the employee's list.
                Id = Guid.Parse("ff010000-0000-0000-0000-000000000303"),
                CycleId = openCycle.Id,
                EmployeeId = priya,
                IsPublished = false,
                Rating = 4.5m,
                Summary = "Draft mid-year calibration output (not yet published)."
            });

        // ---- training catalogue ----------------------------------------------
        var security = new TrainingCourse
        {
            Id = Guid.Parse("ff020000-0000-0000-0000-000000000001"),
            Title = "Security Awareness 2026",
            Description = "Annual information-security and phishing-awareness training.",
            Category = "Compliance", DurationHours = 1.5m, Mandatory = true,
            DueDate = new DateOnly(2026, 12, 15), IsActive = true
        };
        var conduct = new TrainingCourse
        {
            Id = Guid.Parse("ff020000-0000-0000-0000-000000000002"),
            Title = "Code of Conduct",
            Description = "Workplace conduct, anti-harassment and ethics.",
            Category = "Compliance", DurationHours = 1m, Mandatory = true,
            // Past due: enrolment is no longer eligible (422) and non-completers show as Overdue.
            DueDate = new DateOnly(2026, 3, 31), IsActive = true
        };
        var timeManagement = new TrainingCourse
        {
            Id = Guid.Parse("ff020000-0000-0000-0000-000000000003"),
            Title = "Time Management Essentials",
            Description = "Practical planning, prioritisation and focus techniques.",
            Category = "Productivity", DurationHours = 4m, Mandatory = false,
            DueDate = new DateOnly(2026, 12, 31), IsActive = true
        };
        var excel = new TrainingCourse
        {
            Id = Guid.Parse("ff020000-0000-0000-0000-000000000004"),
            Title = "Advanced Excel for Engineers",
            Description = "Pivot tables, Power Query and dashboarding for engineering metrics.",
            Category = "Tools", DurationHours = 6m, Mandatory = false,
            DueDate = new DateOnly(2026, 12, 31), IsActive = true
        };
        var safety = new TrainingCourse
        {
            Id = Guid.Parse("ff020000-0000-0000-0000-000000000005"),
            Title = "Workplace Safety",
            Description = "Office ergonomics, emergency procedures and incident reporting.",
            Category = "Compliance", DurationHours = 1m, Mandatory = true,
            DueDate = new DateOnly(2026, 11, 30), IsActive = true
        };
        var communication = new TrainingCourse
        {
            Id = Guid.Parse("ff020000-0000-0000-0000-000000000006"),
            Title = "Effective Communication",
            Description = "Written and verbal communication for distributed teams.",
            Category = "Soft skills", DurationHours = 3m, Mandatory = false,
            DueDate = new DateOnly(2026, 12, 31), IsActive = true
        };
        _db.Set<TrainingCourse>().AddRange(security, conduct, timeManagement, excel, safety, communication);

        // ---- enrolments (mixed states) ----------------------------------------
        _db.Set<TrainingEnrollment>().AddRange(
            new TrainingEnrollment // Priya completed the mandatory course.
            {
                Id = Guid.Parse("ff030000-0000-0000-0000-000000000001"),
                CourseId = conduct.Id, EmployeeId = priya,
                Status = TrainingEnrollmentStatus.Completed,
                EnrolledAt = new DateTime(2026, 1, 20, 9, 0, 0, DateTimeKind.Utc),
                CompletedAt = new DateTime(2026, 2, 10, 16, 45, 0, DateTimeKind.Utc),
                Score = 92m
            },
            new TrainingEnrollment // Priya enrolled in an optional course.
            {
                Id = Guid.Parse("ff030000-0000-0000-0000-000000000002"),
                CourseId = excel.Id, EmployeeId = priya,
                Status = TrainingEnrollmentStatus.Enrolled,
                EnrolledAt = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc)
            },
            new TrainingEnrollment // Rohit enrolled in the upcoming mandatory safety course.
            {
                Id = Guid.Parse("ff030000-0000-0000-0000-000000000003"),
                CourseId = safety.Id, EmployeeId = rohit,
                Status = TrainingEnrollmentStatus.Enrolled,
                EnrolledAt = new DateTime(2026, 10, 8, 11, 0, 0, DateTimeKind.Utc)
            },
            new TrainingEnrollment // Rohit completed an optional course.
            {
                Id = Guid.Parse("ff030000-0000-0000-0000-000000000004"),
                CourseId = communication.Id, EmployeeId = rohit,
                Status = TrainingEnrollmentStatus.Completed,
                EnrolledAt = new DateTime(2026, 9, 5, 9, 0, 0, DateTimeKind.Utc),
                CompletedAt = new DateTime(2026, 9, 20, 17, 30, 0, DateTimeKind.Utc),
                Score = 88m
            });

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded performance cycles, {Goals} goals, reviews and {Courses} training courses.",
            goals.Count, 6);
    }
}
