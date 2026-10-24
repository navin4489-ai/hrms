using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Auditing;
using Wintech.Hrms.Api.Common.CurrentUser;
using Wintech.Hrms.Api.Common.ErrorHandling;
using Wintech.Hrms.Api.Common.Notifications;
using Wintech.Hrms.Api.Common.Paging;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Approvals;
using Wintech.Hrms.Api.Domain.Employees;
using Wintech.Hrms.Api.Domain.Identity;
using Wintech.Hrms.Api.Domain.Lifecycle;
using Wintech.Hrms.Api.Security;

namespace Wintech.Hrms.Api.Modules.Lifecycle;

/// <summary>
/// Implements contract §21 (resignation &amp; exit) and §22 (onboarding/offboarding checklists).
/// Own-record scoping via <see cref="ICurrentUser"/>; every state change is audited through
/// <see cref="IAuditService"/> and surfaced through <see cref="INotificationPublisher"/>.
/// </summary>
public sealed class LifecycleService : ILifecycleService
{
    private const string ResignationRequestType = "Resignation";
    private const string NoticeDaysKey = "Resignation.NoticeDays";
    private const string PostExitDaysKey = "Access.PostExitDays";
    private const int DefaultNoticeDays = 30;
    private const int DefaultPostExitDays = 30;
    private const int SelfHealingWindowDays = 90;
    private const string ActiveStatus = "Active";
    private const string CompletedStatus = "Completed";

    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly INotificationPublisher _notifications;
    private readonly IClock _clock;
    private readonly IServiceProvider _services;
    private readonly ILogger<LifecycleService> _logger;

    public LifecycleService(AppDbContext db, ICurrentUser currentUser, IAuditService audit,
        INotificationPublisher notifications, IClock clock, IServiceProvider services,
        ILogger<LifecycleService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _notifications = notifications;
        _clock = clock;
        _services = services;
        _logger = logger;
    }

    // ================================================================ configuration (lc_config)

    /// <summary>
    /// Reads a module-owned configuration row; falls back to the code default when the row is
    /// missing (BR-012: configuration changes apply prospectively). Never touches Domain.Admin.
    /// </summary>
    private async Task<int> GetSettingIntAsync(string key, int fallback, CancellationToken cancellationToken)
    {
        var raw = await _db.Set<LifecycleSetting>().AsNoTracking()
            .Where(s => s.Key == key)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);

        return raw is not null &&
               int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) &&
               value > 0
            ? value
            : fallback;
    }

    /// <summary>FR-EMP-133: post-exit access window (days) — consumed by the account status rules.</summary>
    internal Task<int> GetPostExitDaysAsync(CancellationToken cancellationToken = default)
        => GetSettingIntAsync(PostExitDaysKey, DefaultPostExitDays, cancellationToken);

    // ================================================================ resignations (contract §21)

    public async Task<ResignationCreateResponse> CreateResignationAsync(CreateResignationRequest request,
        CancellationToken cancellationToken = default)
    {
        var employeeId = _currentUser.EmployeeId
            ?? throw new ForbiddenException("Your employee record could not be found.");
        var today = _clock.Today;

        if (request.LastWorkingDay is null)
            throw new RequestValidationException(errors: new() { ["lastWorkingDay"] = ["A last working day is required."] });
        if (request.ReasonCategory is null)
            throw new RequestValidationException(errors: new() { ["reasonCategory"] = ["A reason category is required."] });

        var lastWorkingDay = request.LastWorkingDay.Value;
        if (lastWorkingDay <= today)
            throw new RequestValidationException("The last working day must be in the future.",
                new() { ["lastWorkingDay"] = ["The last working day must be in the future."] });

        // FR-EMP-130: one active (Pending/Approved) resignation per employee.
        var hasActive = await _db.Set<Resignation>().AsNoTracking().AnyAsync(r =>
            r.EmployeeId == employeeId &&
            (r.Status == ResignationStatus.Pending || r.Status == ResignationStatus.Approved), cancellationToken);
        if (hasActive)
            throw new ConflictException(
                "You already have an active resignation request. Withdraw it before submitting another one.");

        // Notice-period policy from lc_config (Resignation.NoticeDays, default 30) -> 422.
        var noticeDays = await GetSettingIntAsync(NoticeDaysKey, DefaultNoticeDays, cancellationToken);
        if (lastWorkingDay.DayNumber - today.DayNumber < noticeDays)
            throw new BusinessRuleException("The last working day violates the notice period policy.",
                new()
                {
                    ["lastWorkingDay"] =
                        [$"Last working day must be at least {noticeDays} days from today (configured notice period)."]
                });

        var now = _clock.UtcNow;
        var resignation = new Resignation
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            Status = ResignationStatus.Pending,
            SubmittedAt = now,
            LastWorkingDay = lastWorkingDay,
            ReasonCategory = request.ReasonCategory.Value,
            Reason = request.Reason.Trim(),
            Comments = string.IsNullOrWhiteSpace(request.Comments) ? null : request.Comments.Trim(),
            NoticePeriodDays = noticeDays
        };

        // Route through the generic approval engine. The engine may not be registered in every
        // deployment (module still being assembled): starting the workflow is best-effort and
        // never blocks a resignation submission — approvalTaskId is simply omitted then.
        Guid? approvalTaskId = null;
        try
        {
            var approvalService = _services.GetService<IApprovalService>();
            if (approvalService is null)
            {
                _logger.LogWarning(
                    "IApprovalService is not registered; resignation {Id} was created without an approval task.",
                    resignation.Id);
            }
            else
            {
                var approvalTask = await approvalService.StartAsync(new ApprovalStartRequest(
                    ResignationRequestType,
                    resignation.Id,
                    $"Resignation - {_currentUser.DisplayName}",
                    _currentUser.RequireUserId(),
                    employeeId,
                    _currentUser.DisplayName), cancellationToken);
                approvalTaskId = approvalTask.Id;
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception,
                "Could not start the approval workflow for resignation {Id}; continuing without an approval task.",
                resignation.Id);
        }

        resignation.ApprovalTaskId = approvalTaskId;

        _db.Set<Resignation>().Add(resignation);
        _db.Set<ResignationEvent>().Add(new ResignationEvent
        {
            Id = Guid.NewGuid(),
            ResignationId = resignation.Id,
            Step = "Submission",
            Action = "Submit",
            ActorName = _currentUser.DisplayName,
            Comment = resignation.Comments,
            At = now
        });
        _audit.Add("Resignation.Submitted", "Resignation", resignation.Id.ToString(), "Success",
            new
            {
                lastWorkingDay = lastWorkingDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                reasonCategory = resignation.ReasonCategory.ToString(),
                noticePeriodDays = noticeDays,
                approvalTaskId
            });

        await _db.SaveChangesAsync(cancellationToken);

        var deepLink = $"/resignations/{resignation.Id}";
        await PublishSafeAsync(await ManagerAndHrRecipientsAsync(employeeId, cancellationToken),
            "Resignation", "Resignation submitted",
            $"{_currentUser.DisplayName} submitted a resignation with last working day {Label(lastWorkingDay)}.",
            deepLink, employeeId, cancellationToken);
        await PublishSafeAsync([_currentUser.UserId], "Resignation", "Resignation submitted",
            $"Your resignation has been sent for approval. Last working day: {Label(lastWorkingDay)}.",
            deepLink, employeeId, cancellationToken);

        return new ResignationCreateResponse
        {
            Id = resignation.Id,
            Status = resignation.Status,
            ApprovalTaskId = approvalTaskId,
            SubmittedAt = resignation.SubmittedAt,
            NoticePeriodDays = noticeDays
        };
    }

    public async Task<IReadOnlyList<ResignationListItem>> ListResignationsAsync(ResignationStatus? status,
        CancellationToken cancellationToken = default)
    {
        var employeeId = _currentUser.EmployeeId;
        if (employeeId is null) return [];

        var query = _db.Set<Resignation>().AsNoTracking().Where(r => r.EmployeeId == employeeId.Value);
        if (status.HasValue) query = query.Where(r => r.Status == status.Value);

        var resignations = await query.OrderByDescending(r => r.SubmittedAt).ToListAsync(cancellationToken);
        if (resignations.Count == 0) return [];

        var progress = await LoadExitProgressAsync(resignations.Select(r => r.Id).ToList(), cancellationToken);
        var approvers = await LoadApprovalInfoAsync(resignations, cancellationToken);

        return resignations
            .Select(r =>
            {
                var approver = approvers.GetValueOrDefault(r.Id) ?? new ApprovalInfo(null, null);
                return MapListItem(r,
                    progress.GetValueOrDefault(r.Id) ?? new ChecklistProgressDto(),
                    approver.Name,
                    approver.TaskId ?? r.ApprovalTaskId);
            })
            .ToList();
    }

    public async Task<ResignationDetailDto> GetResignationAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var resignation = await RequireResignationAsync(id, cancellationToken);

        var progress = (await LoadExitProgressAsync([resignation.Id], cancellationToken))
            .GetValueOrDefault(resignation.Id) ?? new ChecklistProgressDto();
        var approver = (await LoadApprovalInfoAsync([resignation], cancellationToken))
            .GetValueOrDefault(resignation.Id) ?? new ApprovalInfo(null, null);

        var events = await _db.Set<ResignationEvent>().AsNoTracking()
            .Where(e => e.ResignationId == id)
            .OrderBy(e => e.At).ThenBy(e => e.Step)
            .Select(e => new DecisionHistoryItemDto
            {
                Step = e.Step,
                Action = e.Action,
                ActorName = e.ActorName,
                Comment = e.Comment,
                At = e.At
            })
            .ToListAsync(cancellationToken);

        var item = MapListItem(resignation, progress, approver.Name, approver.TaskId ?? resignation.ApprovalTaskId);

        return new ResignationDetailDto
        {
            Id = item.Id,
            Status = item.Status,
            SubmittedAt = item.SubmittedAt,
            LastWorkingDay = item.LastWorkingDay,
            LastWorkingDayLabel = item.LastWorkingDayLabel,
            ReasonCategory = item.ReasonCategory,
            Reason = item.Reason,
            ApprovalTaskId = item.ApprovalTaskId,
            CurrentApproverName = item.CurrentApproverName,
            RelievingDate = item.RelievingDate,
            ChecklistProgress = item.ChecklistProgress,
            Comments = resignation.Comments,
            DecisionHistory = events,
            ExitChecklist = await LoadExitItemsAsync(id, cancellationToken)
        };
    }

    public async Task<WithdrawResponse> WithdrawResignationAsync(Guid id, string reason,
        CancellationToken cancellationToken = default)
    {
        var resignation = await RequireResignationAsync(id, cancellationToken);
        if (resignation.Status != ResignationStatus.Pending)
            throw new ConflictException(
                $"The resignation has already been decided ({resignation.Status}) and can no longer be withdrawn.");

        var now = _clock.UtcNow;
        resignation.Status = ResignationStatus.Withdrawn;
        resignation.WithdrawnAt = now;
        resignation.WithdrawReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        _db.Set<ResignationEvent>().Add(new ResignationEvent
        {
            Id = Guid.NewGuid(),
            ResignationId = id,
            Step = "Withdrawal",
            Action = "Withdraw",
            ActorName = _currentUser.DisplayName,
            Comment = resignation.WithdrawReason,
            At = now
        });
        _audit.Add("Resignation.Withdrawn", "Resignation", id.ToString(), "Success",
            new { reason = resignation.WithdrawReason });

        try
        {
            var approvalService = _services.GetService<IApprovalService>();
            if (approvalService is not null)
                await approvalService.CancelAsync(ResignationRequestType, id, resignation.WithdrawReason, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not cancel the approval workflow for resignation {Id}.", id);
        }

        await _db.SaveChangesAsync(cancellationToken);

        await PublishSafeAsync(await HrRecipientsAsync(cancellationToken), "Resignation", "Resignation withdrawn",
            $"{_currentUser.DisplayName} withdrew their resignation.", $"/resignations/{id}",
            resignation.EmployeeId, cancellationToken);

        return new WithdrawResponse { Status = resignation.Status };
    }

    public async Task<IReadOnlyList<ExitChecklistItemDto>> GetExitChecklistAsync(Guid id,
        CancellationToken cancellationToken = default)
    {
        await RequireResignationAsync(id, cancellationToken);
        return await LoadExitItemsAsync(id, cancellationToken);
    }

    public async Task<ExitChecklistItemDto> CompleteExitItemAsync(Guid id, Guid itemId,
        CompleteExitItemRequest request, CancellationToken cancellationToken = default)
    {
        var resignation = await RequireResignationAsync(id, cancellationToken);
        var item = await _db.Set<ExitChecklistItem>()
                .FirstOrDefaultAsync(i => i.Id == itemId && i.ResignationId == id, cancellationToken)
            ?? throw new NotFoundException("The requested checklist item was not found.");

        if (item.Status is ExitChecklistStatus.Completed or ExitChecklistStatus.Waived)
            throw new ConflictException("This checklist item has already been completed.");
        if (!await IsAssignedToCallerAsync(resignation, item, cancellationToken))
            throw new ConflictException("This checklist item is not assigned to you.");

        var now = _clock.UtcNow;
        item.Status = ExitChecklistStatus.Completed;
        item.CompletedAt = now;
        item.CompletedByUserId = _currentUser.UserId;
        if (!string.IsNullOrWhiteSpace(request.Note)) item.Note = request.Note.Trim();
        if (!string.IsNullOrWhiteSpace(request.AttachmentUrl)) item.AttachmentUrl = request.AttachmentUrl.Trim();

        _audit.Add("ExitChecklistItem.Completed", "ExitChecklistItem", item.Id.ToString(), "Success",
            new { resignationId = id, task = item.Task.ToString(), owner = item.Owner.ToString() });
        await _db.SaveChangesAsync(cancellationToken);

        return MapExitItem(item);
    }

    public async Task<ExitSurveyDto> SubmitExitSurveyAsync(Guid id, ExitSurveyRequest request,
        CancellationToken cancellationToken = default)
    {
        var resignation = await RequireResignationAsync(id, cancellationToken);
        if (resignation.Status != ResignationStatus.Approved)
            throw new ConflictException("The exit survey is available only after the resignation has been approved.");
        if (await _db.Set<ExitSurvey>().AsNoTracking().AnyAsync(s => s.ResignationId == id, cancellationToken))
            throw new ConflictException("The exit survey has already been submitted.");

        var now = _clock.UtcNow;
        var survey = new ExitSurvey
        {
            Id = Guid.NewGuid(),
            ResignationId = id,
            OverallRating = request.OverallRating,
            RecommendScore = request.RecommendScore,
            Likes = string.IsNullOrWhiteSpace(request.Likes) ? null : request.Likes.Trim(),
            Improvements = string.IsNullOrWhiteSpace(request.Improvements) ? null : request.Improvements.Trim(),
            WouldRejoin = request.WouldRejoin,
            SubmittedByUserId = _currentUser.UserId,
            SubmittedAt = now
        };
        _db.Set<ExitSurvey>().Add(survey);

        // Submitting the survey also completes the ExitSurvey item on the exit checklist (FR-EMP-132).
        var surveyItem = await _db.Set<ExitChecklistItem>()
            .FirstOrDefaultAsync(i => i.ResignationId == id && i.Task == ExitTaskKind.ExitSurvey, cancellationToken);
        if (surveyItem is { Status: ExitChecklistStatus.NotStarted or ExitChecklistStatus.InProgress })
        {
            surveyItem.Status = ExitChecklistStatus.Completed;
            surveyItem.CompletedAt = now;
            surveyItem.CompletedByUserId = _currentUser.UserId;
            surveyItem.Note = "Exit survey submitted.";
        }

        _audit.Add("ExitSurvey.Submitted", "ExitSurvey", survey.Id.ToString(), "Success",
            new
            {
                resignationId = id,
                overallRating = request.OverallRating,
                recommendScore = request.RecommendScore,
                wouldRejoin = request.WouldRejoin
            });
        await _db.SaveChangesAsync(cancellationToken);

        await PublishSafeAsync(await HrRecipientsAsync(cancellationToken), "Resignation", "Exit survey submitted",
            $"An exit survey was submitted for {Label(resignation.LastWorkingDay)} exit.",
            $"/resignations/{id}", resignation.EmployeeId, cancellationToken);

        return new ExitSurveyDto
        {
            Id = survey.Id,
            ResignationId = survey.ResignationId,
            OverallRating = survey.OverallRating,
            RecommendScore = survey.RecommendScore,
            Likes = survey.Likes,
            Improvements = survey.Improvements,
            WouldRejoin = survey.WouldRejoin,
            SubmittedAt = survey.SubmittedAt
        };
    }

    // ================================================================ checklists (contract §22)

    public async Task<PagedResult<ChecklistListItem>> ListChecklistsAsync(PageRequest page,
        LifecycleChecklistType? type, string? status, string? q, CancellationToken cancellationToken = default)
    {
        // FR-ADM-025: reading the list is self-healing for joiners in the last 90 days.
        await EnsureSelfHealingAsync(cancellationToken);

        var employees = _db.Set<Employee>();
        var query = _db.Set<LifecycleChecklist>().AsNoTracking();
        if (type.HasValue) query = query.Where(c => c.Type == type.Value);

        if (!string.IsNullOrWhiteSpace(status))
        {
            var wantCompleted = status.Trim().Equals(CompletedStatus, StringComparison.OrdinalIgnoreCase);
            query = query.Where(c => wantCompleted ? c.Status == CompletedStatus : c.Status == ActiveStatus);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(c => employees.Any(e => e.Id == c.EmployeeId &&
                (e.DisplayName.ToLower().Contains(term) || e.EmployeeNumber.ToLower().Contains(term))));
        }

        query = query.OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id);

        var paged = await query.ToPagedAsync(page, c => c, cancellationToken);
        if (paged.Items.Count == 0)
            return PagedResult<ChecklistListItem>.Create([], paged.Page, paged.PageSize, paged.Total);

        var ids = paged.Items.Select(c => c.Id).ToList();
        var employeeIds = paged.Items.Select(c => c.EmployeeId).Distinct().ToList();
        var counts = await LoadTaskCountsAsync(ids, cancellationToken);
        var summaries = await LoadEmployeeSummariesAsync(employeeIds, cancellationToken);
        var today = _clock.Today;

        var items = paged.Items
            .Select(c => MapChecklist(c,
                summaries.GetValueOrDefault(c.EmployeeId),
                counts.GetValueOrDefault(c.Id) ?? new TaskCounts(0, 0, 0),
                today))
            .ToList();

        return PagedResult<ChecklistListItem>.Create(items, paged.Page, paged.PageSize, paged.Total);
    }

    public async Task<ChecklistDetailDto> CreateChecklistAsync(CreateChecklistRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.EmployeeId is null)
            throw new RequestValidationException(errors: new() { ["employeeId"] = ["An employee id is required."] });
        if (request.Type is null)
            throw new RequestValidationException(errors: new() { ["type"] = ["A checklist type is required."] });

        var employeeId = request.EmployeeId.Value;
        var type = request.Type.Value;
        var employee = await _db.Set<Employee>().AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken)
            ?? throw new NotFoundException("The requested employee was not found.");

        var activeExists = await _db.Set<LifecycleChecklist>().AsNoTracking().AnyAsync(c =>
            c.EmployeeId == employeeId && c.Type == type && c.Status == ActiveStatus, cancellationToken);
        if (activeExists)
            throw new ConflictException(
                $"An active {(type == LifecycleChecklistType.Onboarding ? "onboarding" : "offboarding")} checklist already exists for this employee.");

        var templateCode = string.IsNullOrWhiteSpace(request.TemplateCode)
            ? DefaultTemplateCode(type)
            : request.TemplateCode.Trim();
        var template = await LoadTemplateAsync(templateCode, cancellationToken);
        if (template is null)
            throw new RequestValidationException("The template code is unknown.",
                new() { ["templateCode"] = ["Unknown template code."] });
        if (template.Type != type)
            throw new RequestValidationException(errors: new() { ["templateCode"] = ["Template type does not match the checklist type."] });

        var startDate = request.StartDate ?? (type == LifecycleChecklistType.Onboarding ? employee.JoinDate : _clock.Today);
        var now = _clock.UtcNow;
        var checklist = BuildChecklist(template, employeeId, type, startDate, now);
        _db.Set<LifecycleChecklist>().Add(checklist);
        _db.Set<LifecycleChecklistEvent>().Add(new LifecycleChecklistEvent
        {
            Id = Guid.NewGuid(),
            ChecklistId = checklist.Id,
            Action = "Created",
            ActorName = _currentUser.DisplayName,
            At = now,
            Note = $"Template {template.Code}"
        });
        _audit.Add("Lifecycle.ChecklistCreated", "LifecycleChecklist", checklist.Id.ToString(), "Success",
            new { employeeId, type = type.ToString(), templateCode = template.Code });
        await _db.SaveChangesAsync(cancellationToken);

        var recipients = new List<Guid?>();
        if (await EmployeeUserAsync(employeeId, cancellationToken) is Guid user) recipients.Add(user);
        recipients.AddRange(await HrRecipientsAsync(cancellationToken));
        await PublishSafeAsync(recipients, "Lifecycle",
            type == LifecycleChecklistType.Onboarding ? "Onboarding checklist assigned" : "Offboarding checklist assigned",
            $"{type} checklist '{template.Name}' was created for {employee.DisplayName}.",
            $"/lifecycle/checklists/{checklist.Id}", employeeId, cancellationToken);

        return await GetChecklistAsync(checklist.Id, cancellationToken);
    }

    public async Task<ChecklistDetailDto> GetChecklistAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var checklist = await _db.Set<LifecycleChecklist>().AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException("The requested checklist was not found.");

        var summary = (await LoadEmployeeSummariesAsync([checklist.EmployeeId], cancellationToken))
            .GetValueOrDefault(checklist.EmployeeId);
        var counts = (await LoadTaskCountsAsync([id], cancellationToken)).GetValueOrDefault(id)
            ?? new TaskCounts(0, 0, 0);
        var today = _clock.Today;

        var tasks = await _db.Set<LifecycleChecklistTask>().AsNoTracking()
            .Where(t => t.ChecklistId == id)
            .OrderBy(t => t.Order).ThenBy(t => t.Id)
            .ToListAsync(cancellationToken);

        var auditTrail = await _db.Set<LifecycleChecklistEvent>().AsNoTracking()
            .Where(e => e.ChecklistId == id)
            .OrderBy(e => e.At).ThenBy(e => e.Id)
            .Select(e => new ChecklistAuditItemDto { Action = e.Action, ActorName = e.ActorName, At = e.At, Note = e.Note })
            .ToListAsync(cancellationToken);

        return ToDetail(MapChecklist(checklist, summary, counts, today),
            tasks.Select(t => MapTask(t, today)).ToList(), auditTrail);
    }

    public async Task<ChecklistTaskDto> TransitionTaskAsync(Guid id, Guid taskId, LifecycleTaskAction action,
        string? note, CancellationToken cancellationToken = default)
    {
        var checklist = await _db.Set<LifecycleChecklist>().FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException("The requested checklist was not found.");
        var task = await _db.Set<LifecycleChecklistTask>()
                .FirstOrDefaultAsync(t => t.Id == taskId && t.ChecklistId == id, cancellationToken)
            ?? throw new NotFoundException("The requested checklist task was not found.");

        var from = task.Status;
        var now = _clock.UtcNow;
        var today = _clock.Today;

        switch (action)
        {
            case LifecycleTaskAction.Start:
                if (from != LifecycleTaskStatus.NotStarted) throw IllegalTransition(action, from);
                task.Status = LifecycleTaskStatus.InProgress;
                break;

            case LifecycleTaskAction.Complete:
                if (from is not (LifecycleTaskStatus.NotStarted or LifecycleTaskStatus.InProgress))
                    throw IllegalTransition(action, from);
                task.Status = LifecycleTaskStatus.Completed;
                task.CompletedAt = now;
                task.CompletedBy = _currentUser.DisplayName;
                if (!string.IsNullOrWhiteSpace(note)) task.Note = note.Trim();
                break;

            case LifecycleTaskAction.Reopen:
                if (from is not (LifecycleTaskStatus.Completed or LifecycleTaskStatus.Waived))
                    throw IllegalTransition(action, from);
                task.Status = LifecycleTaskStatus.NotStarted;
                task.CompletedAt = null;
                task.CompletedBy = null;
                if (!string.IsNullOrWhiteSpace(note)) task.Note = note.Trim();
                break;

            case LifecycleTaskAction.Waive:
                if (from is not (LifecycleTaskStatus.NotStarted or LifecycleTaskStatus.InProgress))
                    throw IllegalTransition(action, from);
                task.Status = LifecycleTaskStatus.Waived;
                task.CompletedAt = now;
                task.CompletedBy = _currentUser.DisplayName;
                if (!string.IsNullOrWhiteSpace(note)) task.Note = note.Trim();
                break;

            default:
                throw IllegalTransition(action, from);
        }

        var to = task.Status;

        // FR-ADM-035: keep the checklist summary status in sync with its tasks.
        var openOthers = await _db.Set<LifecycleChecklistTask>().AsNoTracking().CountAsync(t =>
            t.ChecklistId == id && t.Id != taskId &&
            t.Status != LifecycleTaskStatus.Completed && t.Status != LifecycleTaskStatus.Waived, cancellationToken);
        var allDone = openOthers == 0 && to is LifecycleTaskStatus.Completed or LifecycleTaskStatus.Waived;
        var newStatus = allDone ? CompletedStatus : ActiveStatus;
        if (checklist.Status != newStatus) checklist.Status = newStatus;

        // Every transition is audited (FR-ADM-035): append-only event row + shared audit trail.
        _db.Set<LifecycleChecklistEvent>().Add(new LifecycleChecklistEvent
        {
            Id = Guid.NewGuid(),
            ChecklistId = id,
            TaskId = taskId,
            Action = action.ToString(),
            ActorName = _currentUser.DisplayName,
            At = now,
            Note = string.IsNullOrWhiteSpace(note) ? $"{from} -> {to}" : note.Trim()
        });
        _audit.Add("Lifecycle.TaskTransition", "LifecycleChecklistTask", taskId.ToString(), "Success",
            new { checklistId = id, action = action.ToString(), from = from.ToString(), to = to.ToString() });

        await _db.SaveChangesAsync(cancellationToken);
        return MapTask(task, today);
    }

    public async Task<IReadOnlyList<LifecycleTemplateDto>> ListTemplatesAsync(CancellationToken cancellationToken = default)
    {
        var templates = await _db.Set<LifecycleTemplate>().AsNoTracking()
            .OrderBy(t => t.Type).ThenBy(t => t.Code)
            .Select(t => new
            {
                t.Id,
                t.Code,
                t.Name,
                t.Type,
                t.Active,
                Tasks = t.Tasks
                    .OrderBy(x => x.Order).ThenBy(x => x.Id)
                    .Select(x => new { x.Code, x.Title, x.Owner, x.Mandatory, x.DueOffsetDays })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        return templates.Select(t => new LifecycleTemplateDto
        {
            Code = t.Code,
            Name = t.Name,
            Type = t.Type,
            Active = t.Active,
            TaskCount = t.Tasks.Count,
            Tasks = t.Tasks.Select(x => new LifecycleTemplateTaskDto
            {
                Code = x.Code,
                Title = x.Title,
                Owner = x.Owner,
                Mandatory = x.Mandatory,
                DueOffsetDays = x.DueOffsetDays
            }).ToList()
        }).ToList();
    }

    // ================================================================ approval engine callbacks

    public async Task ApplyApprovalOutcomeAsync(ApprovalTask approvalTask, ApprovalActionType finalAction,
        string? comment, CancellationToken cancellationToken = default)
    {
        var resignation = await _db.Set<Resignation>()
            .FirstOrDefaultAsync(r => r.Id == approvalTask.RequestId, cancellationToken);
        if (resignation is null)
        {
            _logger.LogWarning("Approval {Action} for unknown resignation {Id}.", finalAction, approvalTask.RequestId);
            return;
        }

        if (resignation.Status != ResignationStatus.Pending)
        {
            _logger.LogInformation("Resignation {Id} is {Status}; ignoring approval outcome {Action}.",
                resignation.Id, resignation.Status, finalAction);
            return;
        }

        var now = _clock.UtcNow;
        var actorName = await ResolveDecisionActorAsync(approvalTask, cancellationToken);
        resignation.ApprovalTaskId ??= approvalTask.Id;

        if (finalAction == ApprovalActionType.Approve)
        {
            // Approved: relieving date + the six-task exit checklist (FR-EMP-131/132).
            resignation.Status = ResignationStatus.Approved;
            resignation.RelievingDate = resignation.LastWorkingDay;
            resignation.DecidedAt = now;
            await CreateExitChecklistIfMissingAsync(resignation, cancellationToken);

            _db.Set<ResignationEvent>().Add(new ResignationEvent
            {
                Id = Guid.NewGuid(),
                ResignationId = resignation.Id,
                Step = "Approval",
                Action = "Approved",
                ActorName = actorName,
                Comment = comment,
                At = now
            });
            _audit.Add("Resignation.Approved", "Resignation", resignation.Id.ToString(), "Success",
                new
                {
                    relievingDate = resignation.RelievingDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    comment
                });
        }
        else
        {
            resignation.Status = ResignationStatus.Rejected;
            resignation.DecidedAt = now;

            _db.Set<ResignationEvent>().Add(new ResignationEvent
            {
                Id = Guid.NewGuid(),
                ResignationId = resignation.Id,
                Step = "Approval",
                Action = "Rejected",
                ActorName = actorName,
                Comment = comment,
                At = now
            });
            _audit.Add("Resignation.Rejected", "Resignation", resignation.Id.ToString(), "Success",
                new { comment });
        }

        await _db.SaveChangesAsync(cancellationToken);

        var deepLink = $"/resignations/{resignation.Id}";
        var approved = finalAction == ApprovalActionType.Approve;
        await PublishSafeAsync([await EmployeeUserAsync(resignation.EmployeeId, cancellationToken)],
            "Resignation",
            approved ? "Resignation approved" : "Resignation rejected",
            approved
                ? $"Your resignation has been approved. Relieving date: {Label(resignation.RelievingDate!.Value)}."
                : $"Your resignation has been rejected.{(string.IsNullOrWhiteSpace(comment) ? "" : $" Comment: {comment}")}",
            deepLink, resignation.EmployeeId, cancellationToken);
        await PublishSafeAsync(await HrRecipientsAsync(cancellationToken), "Resignation",
            approved ? "Resignation approved" : "Resignation rejected",
            $"{(await EmployeeNameAsync(resignation.EmployeeId, cancellationToken))}'s resignation was {(approved ? "approved" : "rejected")}.",
            deepLink, resignation.EmployeeId, cancellationToken);
    }

    public async Task CancelResignationAsync(ApprovalTask approvalTask, string? reason,
        CancellationToken cancellationToken = default)
    {
        var resignation = await _db.Set<Resignation>()
            .FirstOrDefaultAsync(r => r.Id == approvalTask.RequestId, cancellationToken);
        if (resignation is null || resignation.Status != ResignationStatus.Pending) return;

        var now = _clock.UtcNow;
        resignation.Status = ResignationStatus.Cancelled;
        resignation.DecidedAt = now;
        resignation.ApprovalTaskId ??= approvalTask.Id;

        _db.Set<ResignationEvent>().Add(new ResignationEvent
        {
            Id = Guid.NewGuid(),
            ResignationId = resignation.Id,
            Step = "Cancellation",
            Action = "Cancelled",
            ActorName = _currentUser.IsAuthenticated ? _currentUser.DisplayName : "Approval engine",
            Comment = reason,
            At = now
        });
        _audit.Add("Resignation.Cancelled", "Resignation", resignation.Id.ToString(), "Success", new { reason });
        await _db.SaveChangesAsync(cancellationToken);

        await PublishSafeAsync([await EmployeeUserAsync(resignation.EmployeeId, cancellationToken)],
            "Resignation", "Resignation cancelled",
            $"Your resignation request was cancelled.{(string.IsNullOrWhiteSpace(reason) ? "" : $" Reason: {reason}")}",
            $"/resignations/{resignation.Id}", resignation.EmployeeId, cancellationToken);
    }

    // ================================================================ internals — resignations

    private async Task<Resignation> RequireResignationAsync(Guid id, CancellationToken cancellationToken)
    {
        var resignation = await _db.Set<Resignation>().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (resignation is null) throw new NotFoundException("The requested resignation was not found.");

        var isOwn = _currentUser.EmployeeId is Guid mine && resignation.EmployeeId == mine;
        if (!isOwn && !_currentUser.HasPermission(Permissions.EmployeeManage))
            throw new NotFoundException("The requested resignation was not found."); // no existence leak
        return resignation;
    }

    private static ResignationListItem MapListItem(Resignation resignation, ChecklistProgressDto progress,
        string? approverName, Guid? approvalTaskId) => new()
    {
        Id = resignation.Id,
        Status = resignation.Status,
        SubmittedAt = resignation.SubmittedAt,
        LastWorkingDay = resignation.LastWorkingDay,
        LastWorkingDayLabel = Label(resignation.LastWorkingDay),
        ReasonCategory = resignation.ReasonCategory,
        Reason = resignation.Reason,
        ApprovalTaskId = approvalTaskId,
        CurrentApproverName = approverName,
        RelievingDate = resignation.RelievingDate,
        ChecklistProgress = progress
    };

    private async Task<Dictionary<Guid, ChecklistProgressDto>> LoadExitProgressAsync(List<Guid> resignationIds,
        CancellationToken cancellationToken)
    {
        var today = _clock.Today;
        var rows = await _db.Set<ExitChecklistItem>().AsNoTracking()
            .Where(i => resignationIds.Contains(i.ResignationId))
            .GroupBy(i => i.ResignationId)
            .Select(g => new
            {
                ResignationId = g.Key,
                Total = g.Count(),
                Completed = g.Count(i => i.Status == ExitChecklistStatus.Completed),
                Overdue = g.Count(i =>
                    (i.Status == ExitChecklistStatus.NotStarted || i.Status == ExitChecklistStatus.InProgress) &&
                    i.DueDate < today)
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.ResignationId,
            r => new ChecklistProgressDto { Total = r.Total, Completed = r.Completed, Overdue = r.Overdue });
    }

    private sealed record ApprovalInfo(Guid? TaskId, string? Name);

    private async Task<Dictionary<Guid, ApprovalInfo>> LoadApprovalInfoAsync(IReadOnlyList<Resignation> resignations,
        CancellationToken cancellationToken)
    {
        var ids = resignations.Select(r => r.Id).ToList();
        var tasks = await _db.Set<ApprovalTask>().AsNoTracking()
            .Where(t => t.RequestType == ResignationRequestType && ids.Contains(t.RequestId))
            .ToListAsync(cancellationToken);
        var byRequest = tasks.GroupBy(t => t.RequestId).ToDictionary(g => g.Key, g => g.ToList());

        var result = new Dictionary<Guid, ApprovalInfo>();
        foreach (var resignation in resignations)
        {
            ApprovalTask? task = resignation.ApprovalTaskId is Guid stored
                ? tasks.FirstOrDefault(t => t.Id == stored)
                : null;
            if (task is null && byRequest.TryGetValue(resignation.Id, out var candidates))
                task = candidates.OrderByDescending(t => t.CreatedAt).First();

            var name = task is { Status: nameof(ApprovalStatus.Pending) }
                ? await ResolveAssigneeNameAsync(task, cancellationToken)
                : null;
            result[resignation.Id] = new ApprovalInfo(task?.Id, name);
        }

        return result;
    }

    private async Task<string?> ResolveAssigneeNameAsync(ApprovalTask task, CancellationToken cancellationToken)
    {
        if (task.CurrentAssigneeUserId is Guid userId)
        {
            var user = await _db.Set<UserAccount>().AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
            if (user is null) return null;
            if (user.EmployeeId is Guid employeeId)
            {
                var name = await _db.Set<Employee>().AsNoTracking()
                    .Where(e => e.Id == employeeId)
                    .Select(e => e.DisplayName)
                    .FirstOrDefaultAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
            return user.UserName;
        }

        return string.IsNullOrWhiteSpace(task.CurrentAssigneeRole) ? null : task.CurrentAssigneeRole;
    }

    private async Task<string> ResolveDecisionActorAsync(ApprovalTask approvalTask, CancellationToken cancellationToken)
    {
        // The engine typically enqueues its action on this same DbContext before invoking the
        // handler, so the unsaved row is visible in the change tracker.
        var queued = _db.ChangeTracker.Entries<ApprovalAction>()
            .Where(e => e.State == EntityState.Added && e.Entity.TaskId == approvalTask.Id)
            .Select(e => e.Entity)
            .OrderByDescending(a => a.At)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(queued?.ActorName)) return queued.ActorName;

        var saved = await _db.Set<ApprovalAction>().AsNoTracking()
            .Where(a => a.TaskId == approvalTask.Id)
            .OrderByDescending(a => a.At)
            .Select(a => a.ActorName)
            .FirstOrDefaultAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(saved) ? "Approval engine" : saved;
    }

    private async Task<IReadOnlyList<ExitChecklistItemDto>> LoadExitItemsAsync(Guid resignationId,
        CancellationToken cancellationToken)
    {
        var items = await _db.Set<ExitChecklistItem>().AsNoTracking()
            .Where(i => i.ResignationId == resignationId)
            .OrderBy(i => i.DueDate).ThenBy(i => i.Task)
            .ToListAsync(cancellationToken);
        return items.Select(MapExitItem).ToList();
    }

    private static ExitChecklistItemDto MapExitItem(ExitChecklistItem item) => new()
    {
        Id = item.Id,
        Task = item.Task,
        Owner = item.Owner,
        Status = item.Status,
        DueDate = item.DueDate,
        CompletedAt = item.CompletedAt,
        Note = item.Note,
        AttachmentUrl = item.AttachmentUrl
    };

    private async Task<bool> IsAssignedToCallerAsync(Resignation resignation, ExitChecklistItem item,
        CancellationToken cancellationToken)
    {
        var isEmployee = _currentUser.EmployeeId is Guid mine && resignation.EmployeeId == mine;
        if (isEmployee) return item.Owner == ExitTaskOwner.Employee;

        // HR override: employee.manage may complete any exit task on the employee's behalf.
        if (_currentUser.HasPermission(Permissions.EmployeeManage)) return true;

        return item.Owner switch
        {
            ExitTaskOwner.Manager => _currentUser.EmployeeId is Guid caller &&
                await _db.Set<Employee>().AsNoTracking()
                    .AnyAsync(e => e.Id == resignation.EmployeeId && e.ManagerId == caller, cancellationToken),
            ExitTaskOwner.HR => _currentUser.HasPermission(Permissions.EmployeeEdit) ||
                                _currentUser.HasPermission(Permissions.EmployeeManage),
            ExitTaskOwner.IT => _currentUser.HasPermission(Permissions.AssetManage),
            ExitTaskOwner.Payroll => _currentUser.HasPermission(Permissions.PayrollManage),
            _ => false
        };
    }

    private async Task CreateExitChecklistIfMissingAsync(Resignation resignation, CancellationToken cancellationToken)
    {
        if (await _db.Set<ExitChecklistItem>().AnyAsync(i => i.ResignationId == resignation.Id, cancellationToken))
            return;

        foreach (var (task, owner, offset) in LifecycleExitDefaults.Tasks)
        {
            _db.Set<ExitChecklistItem>().Add(new ExitChecklistItem
            {
                Id = Guid.NewGuid(),
                ResignationId = resignation.Id,
                Task = task,
                Owner = owner,
                Status = ExitChecklistStatus.NotStarted,
                DueDate = resignation.LastWorkingDay.AddDays(offset)
            });
        }

        _audit.Add("ExitChecklist.Created", "Resignation", resignation.Id.ToString(), "Success",
            new
            {
                tasks = LifecycleExitDefaults.Tasks.Count,
                lastWorkingDay = resignation.LastWorkingDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            });
    }

    // ================================================================ internals — checklists

    private static string DefaultTemplateCode(LifecycleChecklistType type) =>
        type == LifecycleChecklistType.Onboarding ? "ONB-DEFAULT" : "OFF-DEFAULT";

    private async Task<LifecycleTemplate?> LoadTemplateAsync(string code, CancellationToken cancellationToken)
    {
        var template = await _db.Set<LifecycleTemplate>().AsNoTracking()
            .FirstOrDefaultAsync(t => t.Code == code && t.Active, cancellationToken);
        if (template is null) return null;

        template.Tasks = await _db.Set<LifecycleTemplateTask>().AsNoTracking()
            .Where(t => t.TemplateId == template.Id)
            .OrderBy(t => t.Order).ThenBy(t => t.Id)
            .ToListAsync(cancellationToken);
        return template;
    }

    /// <summary>FR-ADM-025: materialise an onboarding checklist for every joiner of the last 90 days.</summary>
    private async Task EnsureSelfHealingAsync(CancellationToken cancellationToken)
    {
        var today = _clock.Today;
        var cutoff = today.AddDays(-SelfHealingWindowDays);

        var recent = await _db.Set<Employee>().AsNoTracking()
            .Where(e => e.JoinDate >= cutoff && e.JoinDate <= today)
            .Select(e => new { e.Id, e.JoinDate })
            .ToListAsync(cancellationToken);
        if (recent.Count == 0) return;

        var existing = await _db.Set<LifecycleChecklist>().AsNoTracking()
            .Where(c => c.Type == LifecycleChecklistType.Onboarding)
            .Select(c => c.EmployeeId)
            .ToListAsync(cancellationToken);
        var existingSet = existing.ToHashSet();
        var missing = recent.Where(e => !existingSet.Contains(e.Id)).ToList();
        if (missing.Count == 0) return;

        var template = await LoadTemplateAsync(DefaultTemplateCode(LifecycleChecklistType.Onboarding), cancellationToken);
        if (template is null)
        {
            _logger.LogWarning("No active onboarding template found; skipping self-healing checklist creation.");
            return;
        }

        var now = _clock.UtcNow;
        foreach (var newcomer in missing)
        {
            var checklist = BuildChecklist(template, newcomer.Id, LifecycleChecklistType.Onboarding,
                newcomer.JoinDate, now);
            _db.Set<LifecycleChecklist>().Add(checklist);
            _audit.Add("Lifecycle.ChecklistCreated", "LifecycleChecklist", checklist.Id.ToString(), "Success",
                new { employeeId = newcomer.Id, type = nameof(LifecycleChecklistType.Onboarding), templateCode = template.Code, selfHealing = true });
        }

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Self-healing created {Count} onboarding checklist(s) for recent joiners (FR-ADM-025).",
            missing.Count);

        await PublishSafeAsync(await HrRecipientsAsync(cancellationToken), "Lifecycle",
            "Onboarding checklists created",
            $"Onboarding checklists were created automatically for {missing.Count} recent joiner(s).",
            "/lifecycle/checklists", null, cancellationToken);
    }

    private LifecycleChecklist BuildChecklist(LifecycleTemplate template, Guid employeeId,
        LifecycleChecklistType type, DateOnly startDate, DateTime now)
    {
        var checklist = new LifecycleChecklist
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            Type = type,
            TemplateCode = template.Code,
            StartDate = startDate,
            Status = ActiveStatus,
            CreatedAt = now
        };

        var templateTasks = template.Tasks.OrderBy(t => t.Order).ThenBy(t => t.Id).ToList();
        var maxOffset = templateTasks.Count > 0 ? templateTasks.Max(t => t.DueOffsetDays) : 0;
        checklist.DueDate = startDate.AddDays(maxOffset);

        foreach (var templateTask in templateTasks)
        {
            _db.Set<LifecycleChecklistTask>().Add(new LifecycleChecklistTask
            {
                Id = Guid.NewGuid(),
                ChecklistId = checklist.Id,
                Code = templateTask.Code,
                Title = templateTask.Title,
                Description = templateTask.Description,
                Owner = templateTask.Owner,
                Mandatory = templateTask.Mandatory,
                Status = LifecycleTaskStatus.NotStarted,
                DueDate = startDate.AddDays(templateTask.DueOffsetDays),
                Order = templateTask.Order
            });
        }

        return checklist;
    }

    private sealed record TaskCounts(int Total, int Completed, int Overdue);

    private async Task<Dictionary<Guid, TaskCounts>> LoadTaskCountsAsync(List<Guid> checklistIds,
        CancellationToken cancellationToken)
    {
        var today = _clock.Today;
        var rows = await _db.Set<LifecycleChecklistTask>().AsNoTracking()
            .Where(t => checklistIds.Contains(t.ChecklistId))
            .GroupBy(t => t.ChecklistId)
            .Select(g => new
            {
                ChecklistId = g.Key,
                Total = g.Count(),
                Completed = g.Count(t =>
                    t.Status == LifecycleTaskStatus.Completed || t.Status == LifecycleTaskStatus.Waived),
                Overdue = g.Count(t =>
                    (t.Status == LifecycleTaskStatus.NotStarted || t.Status == LifecycleTaskStatus.InProgress) &&
                    t.DueDate != null && t.DueDate < today)
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.ChecklistId,
            r => new TaskCounts(r.Total, r.Completed, r.Overdue));
    }

    private sealed record EmployeeSummary(string EmployeeNumber, string EmployeeName, string? Designation,
        string? ManagerName);

    private async Task<Dictionary<Guid, EmployeeSummary>> LoadEmployeeSummariesAsync(List<Guid> employeeIds,
        CancellationToken cancellationToken)
    {
        if (employeeIds.Count == 0) return [];

        var rows = await _db.Set<Employee>().AsNoTracking()
            .Where(e => employeeIds.Contains(e.Id))
            .Select(e => new
            {
                e.Id,
                e.EmployeeNumber,
                e.DisplayName,
                Designation = e.Designation != null ? e.Designation.Name : null,
                ManagerName = e.Manager != null ? e.Manager.DisplayName : null
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.Id,
            r => new EmployeeSummary(r.EmployeeNumber, r.DisplayName, r.Designation, r.ManagerName));
    }

    private static ChecklistListItem MapChecklist(LifecycleChecklist checklist, EmployeeSummary? employee,
        TaskCounts counts, DateOnly today)
    {
        var allDone = counts.Total > 0 && counts.Completed >= counts.Total;
        return new ChecklistListItem
        {
            Id = checklist.Id,
            Type = checklist.Type,
            EmployeeId = checklist.EmployeeId,
            EmployeeNumber = employee?.EmployeeNumber ?? string.Empty,
            EmployeeName = employee?.EmployeeName ?? string.Empty,
            Designation = employee?.Designation,
            ManagerName = employee?.ManagerName,
            Stage = StageFor(checklist.Type, checklist.StartDate, today, allDone),
            ProgressPercent = counts.Total == 0 ? 0 : (int)Math.Round(100.0 * counts.Completed / counts.Total),
            StartDate = checklist.StartDate,
            DueDate = checklist.DueDate,
            OverdueCount = counts.Overdue,
            TasksTotal = counts.Total,
            TasksCompleted = counts.Completed
        };
    }

    private static LifecycleStage StageFor(LifecycleChecklistType type, DateOnly startDate, DateOnly today,
        bool allDone)
    {
        if (allDone) return LifecycleStage.Completed;
        if (type == LifecycleChecklistType.Offboarding) return LifecycleStage.Exit;
        if (today < startDate) return LifecycleStage.PreBoarding;
        return today == startDate ? LifecycleStage.Day1 : LifecycleStage.Week1;
    }

    private static ChecklistTaskDto MapTask(LifecycleChecklistTask task, DateOnly today) => new()
    {
        Id = task.Id,
        Code = task.Code,
        Title = task.Title,
        Description = task.Description,
        Owner = task.Owner,
        Mandatory = task.Mandatory,
        Status = DisplayStatus(task, today),
        DueDate = task.DueDate,
        CompletedAt = task.CompletedAt,
        CompletedBy = task.CompletedBy,
        Note = task.Note,
        Order = task.Order
    };

    /// <summary>Overdue is derived at read time so the stored status keeps its legal transitions.</summary>
    private static LifecycleTaskStatus DisplayStatus(LifecycleChecklistTask task, DateOnly today) =>
        (task.Status is LifecycleTaskStatus.NotStarted or LifecycleTaskStatus.InProgress) &&
        task.DueDate is { } due && due < today
            ? LifecycleTaskStatus.Overdue
            : task.Status;

    private static ChecklistDetailDto ToDetail(ChecklistListItem item, IReadOnlyList<ChecklistTaskDto> tasks,
        IReadOnlyList<ChecklistAuditItemDto> auditTrail) => new()
    {
        Id = item.Id,
        Type = item.Type,
        EmployeeId = item.EmployeeId,
        EmployeeNumber = item.EmployeeNumber,
        EmployeeName = item.EmployeeName,
        Designation = item.Designation,
        ManagerName = item.ManagerName,
        Stage = item.Stage,
        ProgressPercent = item.ProgressPercent,
        StartDate = item.StartDate,
        DueDate = item.DueDate,
        OverdueCount = item.OverdueCount,
        TasksTotal = item.TasksTotal,
        TasksCompleted = item.TasksCompleted,
        Tasks = tasks,
        AuditTrail = auditTrail
    };

    private static ConflictException IllegalTransition(LifecycleTaskAction action, LifecycleTaskStatus current) =>
        new($"'{action}' is not a valid transition from '{current}' status.");

    // ================================================================ notifications & helpers

    private async Task<List<Guid?>> HrRecipientsAsync(CancellationToken cancellationToken)
    {
        var ids = await (
            from userRole in _db.Set<UserRole>()
            join role in _db.Set<Role>() on userRole.RoleId equals role.Id
            where role.Name == Roles.HRHead || role.Name == Roles.HRExecutive || role.Name == Roles.SuperAdmin
            select userRole.UserId).Distinct().ToListAsync(cancellationToken);
        return ids.Cast<Guid?>().ToList();
    }

    private async Task<List<Guid?>> ManagerAndHrRecipientsAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var recipients = await HrRecipientsAsync(cancellationToken);

        var managerId = await _db.Set<Employee>().AsNoTracking()
            .Where(e => e.Id == employeeId)
            .Select(e => e.ManagerId)
            .FirstOrDefaultAsync(cancellationToken);
        if (managerId is Guid mid)
        {
            var managerUser = await _db.Set<UserAccount>().AsNoTracking()
                .Where(u => u.EmployeeId == mid && u.Status == UserStatus.Active)
                .Select(u => (Guid?)u.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (managerUser is Guid uid && !recipients.Contains(uid)) recipients.Add(uid);
        }

        return recipients;
    }

    private async Task<Guid?> EmployeeUserAsync(Guid employeeId, CancellationToken cancellationToken) =>
        await _db.Set<UserAccount>().AsNoTracking()
            .Where(u => u.EmployeeId == employeeId && u.Status == UserStatus.Active)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<string> EmployeeNameAsync(Guid employeeId, CancellationToken cancellationToken) =>
        await _db.Set<Employee>().AsNoTracking()
            .Where(e => e.Id == employeeId)
            .Select(e => e.DisplayName)
            .FirstOrDefaultAsync(cancellationToken) ?? "An employee";

    private async Task PublishSafeAsync(IEnumerable<Guid?> userIds, string type, string title, string body,
        string? deepLink, Guid? employeeId, CancellationToken cancellationToken)
    {
        foreach (var userId in userIds)
        {
            try
            {
                await _notifications.PublishAsync(
                    new NotificationMessage(userId, type, title, body, deepLink, "HR", false, employeeId),
                    cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Notification '{Title}' to {User} could not be published.",
                    title, userId?.ToString() ?? "all");
            }
        }
    }

    private static string Label(DateOnly date) => date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
}
