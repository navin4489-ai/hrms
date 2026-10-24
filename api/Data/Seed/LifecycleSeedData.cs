using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Registration;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Employees;
using Wintech.Hrms.Api.Domain.Identity;
using Wintech.Hrms.Api.Domain.Lifecycle;

namespace Wintech.Hrms.Api.Data.Seed;

/// <summary>
/// Idempotent Lifecycle seed (Order 160, after core):
/// 1. module configuration rows in lc_config (Resignation.NoticeDays, Access.PostExitDays);
/// 2. default onboarding + offboarding templates with their tasks;
/// 3. only when no resignation exists at all: one historical approved/completed resignation with a
///    finished exit checklist and survey for a demo employee, so mobile screens have data.
/// </summary>
public sealed class LifecycleSeedData : IDataSeeder
{
    public const string OnboardingTemplateCode = "ONB-DEFAULT";
    public const string OffboardingTemplateCode = "OFF-DEFAULT";

    private readonly AppDbContext _db;
    private readonly ILogger<LifecycleSeedData> _logger;

    public LifecycleSeedData(AppDbContext db, ILogger<LifecycleSeedData> logger)
    {
        _db = db;
        _logger = logger;
    }

    public int Order => 160;

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSettingsAsync(cancellationToken);
        await EnsureTemplatesAsync(cancellationToken);
        await EnsureHistoricalResignationAsync(cancellationToken);
    }

    // ---------------------------------------------------------------- configuration

    private async Task EnsureSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = new (string Key, string Value, string Description)[]
        {
            ("Resignation.NoticeDays", "30",
                "Minimum notice period in days between today and the proposed last working day."),
            ("Access.PostExitDays", "30",
                "Days an ex-employee may keep read access to permitted documents after exit (FR-EMP-133).")
        };

        foreach (var (key, value, description) in settings)
        {
            if (await _db.Set<LifecycleSetting>().AnyAsync(s => s.Key == key, cancellationToken)) continue;
            _db.Set<LifecycleSetting>().Add(new LifecycleSetting
            {
                Id = Guid.NewGuid(),
                Key = key,
                Value = value,
                Description = description
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    // ---------------------------------------------------------------- templates

    private async Task EnsureTemplatesAsync(CancellationToken cancellationToken)
    {
        var templates = new (string Code, string Name, LifecycleChecklistType Type,
            (string Code, string Title, string? Description, LifecycleTaskOwner Owner, bool Mandatory, int Offset)[] Tasks)[]
        {
            (OnboardingTemplateCode, "Default onboarding", LifecycleChecklistType.Onboarding,
            [
                ("PREBOARD-DOCS", "Collect pre-boarding documents",
                    "Identity, education and reference documents collected and verified.",
                    LifecycleTaskOwner.HR, true, -5),
                ("ACCOUNTS", "Create email and system accounts",
                    "Work email, HRMS login and tooling accounts provisioned (FR-ADM-034).",
                    LifecycleTaskOwner.IT, true, 0),
                ("ASSET-SETUP", "Assign laptop and accessories",
                    "Laptop, monitor and access card issued against the asset register.",
                    LifecycleTaskOwner.IT, true, 0),
                ("ORIENTATION", "Welcome and orientation",
                    "Introductory session with the manager and the team.",
                    LifecycleTaskOwner.Manager, true, 0),
                ("POLICY-ACK", "Policy handbook acknowledgement",
                    "Handbook and code of conduct acknowledged in writing.",
                    LifecycleTaskOwner.Employee, true, 1),
                ("PAYROLL-FORMS", "Complete payroll and tax forms",
                    "Bank details, PAN and tax declarations collected (FR-ADM-032).",
                    LifecycleTaskOwner.Payroll, true, 3),
                ("PROBATION-PLAN", "Schedule probation check-in",
                    "30/60/90 day check-ins scheduled with the manager.",
                    LifecycleTaskOwner.Manager, false, 7)
            ]),
            (OffboardingTemplateCode, "Default offboarding", LifecycleChecklistType.Offboarding,
            [
                ("HANDOVER", "Knowledge handover to successor",
                    "Repositories, credentials and open work handed over.",
                    LifecycleTaskOwner.Manager, true, -7),
                ("ASSET-RETURN", "Return company assets",
                    "Laptop, badge and accessories returned to IT.",
                    LifecycleTaskOwner.IT, true, 0),
                ("ACCESS-REVOKE", "Revoke system access",
                    "Accounts disabled on the last working day (FR-EMP-133).",
                    LifecycleTaskOwner.IT, true, 0),
                ("EXIT-SURVEY", "Complete exit survey",
                    "Employee completes the exit survey (FR-EMP-132).",
                    LifecycleTaskOwner.Employee, true, -3),
                ("DOCS-ACK", "Acknowledge exit documents",
                    "Relieving letter and clearance documents acknowledged.",
                    LifecycleTaskOwner.HR, true, -1),
                ("FULL-FINAL", "Full and final settlement",
                    "Pending dues, leave encashment and final payroll prepared.",
                    LifecycleTaskOwner.Payroll, true, 3)
            ])
        };

        foreach (var (code, name, type, taskDefs) in templates)
        {
            if (await _db.Set<LifecycleTemplate>().AnyAsync(t => t.Code == code, cancellationToken)) continue;

            var template = new LifecycleTemplate
            {
                Id = Guid.NewGuid(),
                Code = code,
                Name = name,
                Type = type,
                Active = true
            };

            var order = 0;
            foreach (var (taskCode, title, description, owner, mandatory, offset) in taskDefs)
            {
                template.Tasks.Add(new LifecycleTemplateTask
                {
                    Id = Guid.NewGuid(),
                    Code = taskCode,
                    Title = title,
                    Description = description,
                    Owner = owner,
                    Mandatory = mandatory,
                    DueOffsetDays = offset,
                    Order = ++order
                });
            }

            _db.Set<LifecycleTemplate>().Add(template);
        }

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogDebug("Lifecycle templates ensured.");
    }

    // ---------------------------------------------------------------- historical resignation

    private async Task EnsureHistoricalResignationAsync(CancellationToken cancellationToken)
    {
        // Only seed demo history when the module has no resignation data at all.
        if (await _db.Set<Resignation>().AnyAsync(cancellationToken)) return;

        // EMP007 (Rohit Verma, user "employee2") has no demo account conflict and lets the
        // employee-side mobile resignation screen show data out of the box.
        var employee = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.EmployeeNumber == "EMP007", cancellationToken);
        if (employee is null)
        {
            _logger.LogWarning("Demo employee EMP007 not found; skipping historical resignation seed.");
            return;
        }

        var submittedAt = new DateTime(2026, 7, 1, 9, 30, 0, DateTimeKind.Utc);
        var decidedAt = new DateTime(2026, 7, 15, 11, 0, 0, DateTimeKind.Utc);
        var lastWorkingDay = new DateOnly(2026, 7, 31);

        var resignation = new Resignation
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            Status = ResignationStatus.Approved,
            SubmittedAt = submittedAt,
            LastWorkingDay = lastWorkingDay,
            ReasonCategory = ResignationReasonCategory.BetterOpportunity,
            Reason = "Accepted an opportunity closer to my family with a stronger career path.",
            Comments = "Happy to support a smooth handover during the notice period.",
            NoticePeriodDays = 30,
            ApprovalTaskId = null,
            RelievingDate = lastWorkingDay,
            DecidedAt = decidedAt
        };
        _db.Set<Resignation>().Add(resignation);

        _db.Set<ResignationEvent>().AddRange(
            new ResignationEvent
            {
                Id = Guid.NewGuid(),
                ResignationId = resignation.Id,
                Step = "Submission",
                Action = "Submit",
                ActorName = employee.DisplayName,
                Comment = resignation.Comments,
                At = submittedAt
            },
            new ResignationEvent
            {
                Id = Guid.NewGuid(),
                ResignationId = resignation.Id,
                Step = "Approval",
                Action = "Approved",
                ActorName = "Meera Iyer",
                Comment = "Approved with the standard 30 day notice period.",
                At = decidedAt
            });

        // Finished exit checklist: every task completed before the last working day.
        var completedOffsets = new Dictionary<ExitTaskKind, (int Offset, string Note)>
        {
            [ExitTaskKind.KnowledgeHandover] = (-7, "Handover document signed off by the successor."),
            [ExitTaskKind.AssetReturn] = (-1, "Laptop, monitor and badge returned to IT."),
            [ExitTaskKind.AccessRevocation] = (0, "Accounts disabled at end of last working day."),
            [ExitTaskKind.ExitSurvey] = (-21, "Exit survey submitted."),
            [ExitTaskKind.DocumentAcknowledgement] = (-1, "Relieving letter and NDA acknowledged."),
            [ExitTaskKind.FullAndFinal] = (0, "Full and final settlement processed by payroll.")
        };

        var completionBase = new DateTime(2026, 7, 20, 10, 0, 0, DateTimeKind.Utc);
        var itemIndex = 0;
        foreach (var (task, owner, offset) in LifecycleExitDefaults.Tasks)
        {
            var (actualOffset, note) = completedOffsets[task];
            _db.Set<ExitChecklistItem>().Add(new ExitChecklistItem
            {
                Id = Guid.NewGuid(),
                ResignationId = resignation.Id,
                Task = task,
                Owner = owner,
                Status = ExitChecklistStatus.Completed,
                DueDate = lastWorkingDay.AddDays(offset),
                CompletedAt = completionBase.AddMinutes(++itemIndex * 45),
                CompletedByUserId = null,
                Note = note
            });
        }

        var submittingUser = await _db.Set<UserAccount>().AsNoTracking()
            .Where(u => u.EmployeeId == employee.Id)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);

        _db.Set<ExitSurvey>().Add(new ExitSurvey
        {
            Id = Guid.NewGuid(),
            ResignationId = resignation.Id,
            OverallRating = 4,
            RecommendScore = 8,
            Likes = "Great colleagues, flexible work culture and supportive managers.",
            Improvements = "Faster internal mobility and clearer growth paths would help retention.",
            WouldRejoin = true,
            SubmittedByUserId = submittingUser ?? Guid.NewGuid(),
            SubmittedAt = submittedAt.AddDays(9)
        });

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Seeded historical resignation {Id} for {Employee} with a completed exit checklist.",
            resignation.Id, employee.EmployeeNumber);
    }
}
