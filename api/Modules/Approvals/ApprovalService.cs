using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Auditing;
using Wintech.Hrms.Api.Common.ErrorHandling;
using Wintech.Hrms.Api.Common.Notifications;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Approvals;
using Wintech.Hrms.Api.Domain.Employees;
using Wintech.Hrms.Api.Domain.Identity;

namespace Wintech.Hrms.Api.Modules.Approvals;

/// <summary>
/// Central approval engine — FR-MGR-001..007, FR-ADM-060..065, BR-003/004/007.
/// <para>
/// Resolves the latest active workflow definition per request type and snapshots its version onto
/// the task, resolves each step's assignee from <see cref="ApproverType"/>, advances multi-step
/// workflows sequentially, records every state change as an <see cref="ApprovalAction"/> plus an
/// audit event, publishes notifications to the new assignee and invokes the matching
/// <see cref="IApprovalHandler"/> exactly once when a workflow reaches a terminal state.
/// Optimistic concurrency is enforced through <see cref="ApprovalTask.Version"/> (EF concurrency
/// token): stale/duplicate decisions surface as 409.
/// </para>
/// </summary>
public sealed class ApprovalService : IApprovalService
{
    /// <summary>Delegations are time-boxed server-side (FR-MGR-003); the client cannot extend them.</summary>
    private static readonly TimeSpan DelegationWindow = TimeSpan.FromDays(7);

    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly INotificationPublisher _notifications;
    private readonly IClock _clock;
    private readonly ILogger<ApprovalService> _logger;
    private readonly IReadOnlyList<IApprovalHandler> _handlers;

    public ApprovalService(AppDbContext db, IAuditService audit, INotificationPublisher notifications,
        IClock clock, ILogger<ApprovalService> logger, IEnumerable<IApprovalHandler> handlers)
    {
        _db = db;
        _audit = audit;
        _notifications = notifications;
        _clock = clock;
        _logger = logger;
        _handlers = handlers.ToList();
    }

    // ---------------------------------------------------------------- StartAsync

    public async Task<ApprovalTask> StartAsync(ApprovalStartRequest request, CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.RequestType))
            errors["requestType"] = ["Request type is required."];
        if (request.RequestId == Guid.Empty)
            errors["requestId"] = ["Request id is required."];
        if (request.RequesterUserId == Guid.Empty)
            errors["requesterUserId"] = ["Requester is required."];
        if (errors.Count > 0)
            throw new RequestValidationException("The approval start request is invalid.", errors);

        var now = _clock.UtcNow;
        var definition = await (
                from w in _db.ApprovalWorkflowDefinitions.AsNoTracking()
                where w.RequestType == request.RequestType && w.IsActive
                orderby w.Version descending
                select new { w.Id, w.Version })
            .FirstOrDefaultAsync(cancellationToken);

        var steps = await LoadStepsAsync(definition?.Id, request.RequestType, cancellationToken);
        var firstStep = steps[0];

        var (assigneeUserId, assigneeRole) =
            await ResolveAssigneeAsync(firstStep, request.RequesterUserId, request.RequesterEmployeeId, cancellationToken);
        if (assigneeUserId == request.RequesterUserId)
        {
            // BR-004: never hand a task to its own requester.
            _logger.LogWarning(
                "Workflow step {Step} for request type {RequestType} resolves to the requester; leaving task unassigned.",
                firstStep.Name, request.RequestType);
            assigneeUserId = null;
        }

        var task = new ApprovalTask
        {
            Id = Guid.NewGuid(),
            RequestType = request.RequestType.Trim(),
            RequestId = request.RequestId,
            Title = string.IsNullOrWhiteSpace(request.Title) ? request.RequestType : request.Title.Trim(),
            RequesterUserId = request.RequesterUserId,
            RequesterEmployeeId = request.RequesterEmployeeId,
            RequesterName = request.RequesterName,
            Amount = request.Amount,
            Currency = request.Currency,
            Status = nameof(ApprovalStatus.Pending),
            CurrentStepNumber = firstStep.StepNumber,
            CurrentStepName = firstStep.Name,
            CurrentAssigneeUserId = assigneeUserId,
            CurrentAssigneeRole = assigneeRole,
            WorkflowDefinitionId = definition?.Id,
            WorkflowVersion = definition?.Version ?? 1,
            CreatedAt = now,
            UpdatedAt = now,
            DueAt = ComputeDueAt(now, firstStep.SLADays)
        };

        task.Actions.Add(new ApprovalAction
        {
            Id = Guid.NewGuid(),
            TaskId = task.Id,
            StepNumber = 0,
            Action = ApprovalActionType.Start,
            ActorUserId = request.RequesterUserId,
            ActorName = string.IsNullOrWhiteSpace(request.RequesterName) ? "Unknown" : request.RequesterName!,
            Comment = null,
            At = now
        });

        _db.ApprovalTasks.Add(task);

        await NotifyAssigneeAsync(task, cancellationToken);
        _audit.Add("Approval.Started", "ApprovalTask", task.Id.ToString(), "Success", new
        {
            task.RequestType,
            RequestId = task.RequestId,
            task.Title,
            task.WorkflowVersion,
            Step = task.CurrentStepNumber
        });

        await SaveAsync(cancellationToken);
        _logger.LogInformation("Started approval {TaskId} for {RequestType}/{RequestId} at step {Step}.",
            task.Id, task.RequestType, task.RequestId, task.CurrentStepNumber);
        return task;
    }

    // ---------------------------------------------------------------- DecideAsync

    public async Task<ApprovalTask> DecideAsync(Guid taskId, ApprovalActionType action, string? comment,
        Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (action is not (ApprovalActionType.Approve or ApprovalActionType.Reject or ApprovalActionType.Return))
            throw new RequestValidationException("Only Approve, Reject or Return can be decided here.",
                new() { ["action"] = ["Must be Approve, Reject or Return."] });

        // BR-007: a rejection/return must always carry a reason.
        if (action is ApprovalActionType.Reject or ApprovalActionType.Return && string.IsNullOrWhiteSpace(comment))
            throw new BusinessRuleException("A comment is required when rejecting or returning a request.",
                new() { ["comment"] = ["A comment is required for Reject/Return (BR-007)."] });

        var task = await _db.ApprovalTasks
                .FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken)
            ?? throw new NotFoundException("The approval task was not found.");

        if (!string.Equals(task.Status, nameof(ApprovalStatus.Pending), StringComparison.Ordinal))
            throw new ConflictException("This approval was already decided and can no longer be changed.");

        // BR-004: a requester never decides their own task.
        if (task.RequesterUserId == actorUserId)
            throw new ForbiddenException("You cannot decide your own request (BR-004).");

        if (task.CurrentAssigneeUserId != actorUserId && task.DelegatedToUserId != actorUserId)
            throw new ForbiddenException("This approval is not assigned to you.");

        var now = _clock.UtcNow;
        var actorName = await DisplayNameAsync(actorUserId, null, cancellationToken);

        task.Actions.Add(new ApprovalAction
        {
            Id = Guid.NewGuid(),
            TaskId = task.Id,
            StepNumber = task.CurrentStepNumber,
            Action = action,
            ActorUserId = actorUserId,
            ActorName = actorName,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
            FromUserId = task.CurrentAssigneeUserId,
            At = now
        });
        task.UpdatedAt = now;

        var auditAction = action switch
        {
            ApprovalActionType.Approve => "Approval.Approved",
            ApprovalActionType.Reject => "Approval.Rejected",
            _ => "Approval.Returned"
        };

        if (action == ApprovalActionType.Approve)
        {
            var steps = await LoadStepsAsync(task.WorkflowDefinitionId, task.RequestType, cancellationToken);
            var next = steps.FirstOrDefault(s => s.StepNumber == task.CurrentStepNumber + 1);
            if (next is not null)
            {
                await AdvanceToAsync(task, next, now, cancellationToken);
            }
            else
            {
                await FinalizeAsync(task, action, comment, now, cancellationToken);
            }
        }
        else
        {
            await FinalizeAsync(task, action, comment, now, cancellationToken);
        }

        _audit.Add(auditAction, "ApprovalTask", task.Id.ToString(), "Success", new
        {
            task.RequestType,
            RequestId = task.RequestId,
            task.CurrentStepNumber,
            task.Status,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim()
        });

        await SaveAsync(cancellationToken);
        _logger.LogInformation("Approval {TaskId}: {Action} by {Actor}; status {Status}.",
            task.Id, action, actorName, task.Status);
        return task;
    }

    // ---------------------------------------------------------------- DelegateAsync

    public async Task<ApprovalTask> DelegateAsync(Guid taskId, Guid toUserId, string? reason,
        Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (toUserId == Guid.Empty)
            throw new RequestValidationException("The delegate is required.",
                new() { ["toUserId"] = ["A valid user id is required."] });

        var task = await _db.ApprovalTasks
                .FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken)
            ?? throw new NotFoundException("The approval task was not found.");

        if (!string.Equals(task.Status, nameof(ApprovalStatus.Pending), StringComparison.Ordinal))
            throw new ConflictException("Only pending approvals can be delegated.");

        if (task.CurrentAssigneeUserId != actorUserId && task.DelegatedToUserId != actorUserId)
            throw new ForbiddenException("This approval is not assigned to you.");

        if (toUserId == actorUserId)
            throw new BusinessRuleException("You cannot delegate an approval to yourself.",
                new() { ["toUserId"] = ["Choose a different approver."] });

        // BR-004: delegating to the requester would enable self-approval.
        if (toUserId == task.RequesterUserId)
            throw new BusinessRuleException("An approval cannot be delegated to the original requester (BR-004).",
                new() { ["toUserId"] = ["Choose a different approver."] });

        var delegateActive = await _db.UserAccounts.AsNoTracking()
            .AnyAsync(u => u.Id == toUserId && u.Status == UserStatus.Active, cancellationToken);
        if (!delegateActive)
            throw new NotFoundException("The delegate user was not found or is not active.");

        var now = _clock.UtcNow;
        var actorName = await DisplayNameAsync(actorUserId, null, cancellationToken);

        task.Actions.Add(new ApprovalAction
        {
            Id = Guid.NewGuid(),
            TaskId = task.Id,
            StepNumber = task.CurrentStepNumber,
            Action = ApprovalActionType.Delegate,
            ActorUserId = actorUserId,
            ActorName = actorName,
            Comment = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            FromUserId = actorUserId,
            ToUserId = toUserId,
            At = now
        });

        task.DelegatedToUserId = toUserId;
        task.DelegatedAt = now;
        task.DelegationExpiresAt = now.Add(DelegationWindow);
        task.UpdatedAt = now;

        var target = await (
                from u in _db.UserAccounts.AsNoTracking()
                where u.Id == toUserId
                select new { u.EmployeeId, Name = u.Employee != null ? u.Employee.DisplayName : u.UserName })
            .FirstOrDefaultAsync(cancellationToken);

        await PublishAsync(toUserId, target?.EmployeeId, "ApprovalDelegated",
            $"Approval delegated: {task.Title}",
            $"{actorName} delegated an approval to you. Review it before it becomes overdue.",
            DeepLink(task.Id), cancellationToken);

        _audit.Add("Approval.Delegated", "ApprovalTask", task.Id.ToString(), "Success", new
        {
            task.RequestType,
            RequestId = task.RequestId,
            From = actorUserId,
            To = toUserId,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            ExpiresAt = task.DelegationExpiresAt
        });

        await SaveAsync(cancellationToken);
        _logger.LogInformation("Approval {TaskId} delegated from {From} to {To} until {ExpiresAt}.",
            task.Id, actorUserId, toUserId, task.DelegationExpiresAt);
        return task;
    }

    // ---------------------------------------------------------------- CancelAsync

    public async Task CancelAsync(string requestType, Guid requestId, string? reason,
        CancellationToken cancellationToken = default)
    {
        var task = await _db.ApprovalTasks
            .FirstOrDefaultAsync(t => t.RequestType == requestType && t.RequestId == requestId &&
                                      t.Status == nameof(ApprovalStatus.Pending), cancellationToken);
        if (task is null)
        {
            _logger.LogDebug("CancelAsync: no pending approval for {RequestType}/{RequestId}; nothing to do.",
                requestType, requestId);
            return;
        }

        var now = _clock.UtcNow;
        var actorName = await DisplayNameAsync(task.RequesterUserId, task.RequesterName, cancellationToken);

        task.Status = nameof(ApprovalStatus.Cancelled);
        task.CompletedAt = now;
        task.UpdatedAt = now;
        task.Actions.Add(new ApprovalAction
        {
            Id = Guid.NewGuid(),
            TaskId = task.Id,
            StepNumber = task.CurrentStepNumber,
            Action = ApprovalActionType.Cancel,
            ActorUserId = task.RequesterUserId,
            ActorName = actorName,
            Comment = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            At = now
        });

        var handler = HandlerFor(task.RequestType);
        if (handler is null)
            _logger.LogWarning("No IApprovalHandler for request type {RequestType}; cancelling task {TaskId} without a domain update.",
                task.RequestType, task.Id);
        else
            await handler.OnCancelledAsync(task, string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(), cancellationToken);

        _audit.Add("Approval.Cancelled", "ApprovalTask", task.Id.ToString(), "Success", new
        {
            task.RequestType,
            RequestId = task.RequestId,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()
        });

        await SaveAsync(cancellationToken);
        _logger.LogInformation("Approval {TaskId} cancelled for {RequestType}/{RequestId}.",
            task.Id, requestType, requestId);
    }

    // ---------------------------------------------------------------- GetForAsync

    public Task<ApprovalTask?> GetForAsync(string requestType, Guid requestId, CancellationToken cancellationToken = default)
        => _db.ApprovalTasks.AsNoTracking()
            .Where(t => t.RequestType == requestType && t.RequestId == requestId)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    // ---------------------------------------------------------------- internals

    /// <summary>Loads the (version-pinned) steps for a task, falling back to a built-in single step.</summary>
    private async Task<List<ApprovalWorkflowStep>> LoadStepsAsync(Guid? workflowId, string requestType,
        CancellationToken cancellationToken)
    {
        var steps = new List<ApprovalWorkflowStep>();
        if (workflowId is not null)
        {
            steps.AddRange(await _db.ApprovalWorkflowSteps.AsNoTracking()
                .Where(s => s.WorkflowId == workflowId)
                .OrderBy(s => s.StepNumber)
                .ToListAsync(cancellationToken));
        }

        if (steps.Count == 0)
        {
            _logger.LogWarning("No workflow steps found for request type {RequestType} (workflow {WorkflowId}); using a default line-manager step.",
                requestType, workflowId?.ToString() ?? "<none>");
            steps.Add(new ApprovalWorkflowStep
            {
                Id = Guid.NewGuid(),
                StepNumber = 1,
                Name = "Line manager",
                ApproverType = ApproverType.Manager,
                SLADays = 3
            });
        }

        return steps;
    }

    private async Task AdvanceToAsync(ApprovalTask task, ApprovalWorkflowStep next, DateTime now,
        CancellationToken cancellationToken)
    {
        var (assigneeUserId, assigneeRole) =
            await ResolveAssigneeAsync(next, task.RequesterUserId, task.RequesterEmployeeId, cancellationToken);
        if (assigneeUserId == task.RequesterUserId)
        {
            _logger.LogWarning("Next step {Step} resolves to the requester for task {TaskId}; leaving unassigned.",
                next.Name, task.Id);
            assigneeUserId = null;
        }

        task.CurrentStepNumber = next.StepNumber;
        task.CurrentStepName = next.Name;
        task.CurrentAssigneeUserId = assigneeUserId;
        task.CurrentAssigneeRole = assigneeRole;
        task.DelegatedToUserId = null;
        task.DelegatedAt = null;
        task.DelegationExpiresAt = null;
        task.DueAt = ComputeDueAt(now, next.SLADays);

        await NotifyAssigneeAsync(task, cancellationToken);
    }

    private async Task FinalizeAsync(ApprovalTask task, ApprovalActionType finalAction, string? comment,
        DateTime now, CancellationToken cancellationToken)
    {
        task.Status = finalAction switch
        {
            ApprovalActionType.Approve => nameof(ApprovalStatus.Approved),
            ApprovalActionType.Reject => nameof(ApprovalStatus.Rejected),
            ApprovalActionType.Return => nameof(ApprovalStatus.Returned),
            _ => task.Status
        };
        task.CompletedAt = now;

        var handler = HandlerFor(task.RequestType);
        if (handler is null)
        {
            // The workflow still completes; the domain record simply has no consumer yet.
            _logger.LogWarning("No IApprovalHandler registered for request type {RequestType}; completing task {TaskId} without a domain update.",
                task.RequestType, task.Id);
            return;
        }

        try
        {
            // Same DbContext/unit of work: the domain update commits together with the task state.
            await handler.OnCompletedAsync(task, finalAction, comment, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "IApprovalHandler for {RequestType} failed on task {TaskId}; the approval was not committed.",
                task.RequestType, task.Id);
            throw;
        }

        await PublishAsync(task.RequesterUserId, task.RequesterEmployeeId,
            $"Approval{task.Status}",
            $"Your request was {task.Status.ToLowerInvariant()}: {task.Title}",
            string.IsNullOrWhiteSpace(comment)
                ? $"Your {task.RequestType} request was {task.Status.ToLowerInvariant()}."
                : $"Your {task.RequestType} request was {task.Status.ToLowerInvariant()}: {comment.Trim()}",
            DeepLink(task.Id), cancellationToken);
    }

    private IApprovalHandler? HandlerFor(string requestType)
        => _handlers.FirstOrDefault(h => string.Equals(h.RequestType, requestType, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Resolves the user (and role, when role-based) that owns a workflow step:
    /// Manager -> requester's line manager; Role -> first active user holding the named role;
    /// NamedUser -> the exact user; SelfAndManager -> manager with a role fallback.
    /// </summary>
    private async Task<(Guid? UserId, string? Role)> ResolveAssigneeAsync(ApprovalWorkflowStep step,
        Guid requesterUserId, Guid? requesterEmployeeId, CancellationToken cancellationToken)
    {
        switch (step.ApproverType)
        {
            case ApproverType.Manager:
            case ApproverType.SelfAndManager:
            {
                var managerUserId = await ResolveManagerUserIdAsync(requesterEmployeeId, cancellationToken);
                if (managerUserId is not null && managerUserId != requesterUserId)
                    return (managerUserId, null);

                if (step.ApproverType == ApproverType.SelfAndManager && !string.IsNullOrWhiteSpace(step.ApproverValue))
                {
                    var fallback = await ResolveRoleUserIdAsync(step.ApproverValue, cancellationToken);
                    if (fallback is not null) return (fallback, step.ApproverValue);
                }

                return (null, null);
            }

            case ApproverType.Role:
            {
                if (string.IsNullOrWhiteSpace(step.ApproverValue))
                {
                    _logger.LogWarning("Role step {Step} has no approver value; task left unassigned.", step.Name);
                    return (null, null);
                }

                var userId = await ResolveRoleUserIdAsync(step.ApproverValue, cancellationToken);
                return (userId, step.ApproverValue);
            }

            case ApproverType.NamedUser:
            {
                if (Guid.TryParse(step.ApproverValue, out var named) && named != requesterUserId)
                {
                    var active = await _db.UserAccounts.AsNoTracking()
                        .AnyAsync(u => u.Id == named && u.Status == UserStatus.Active, cancellationToken);
                    if (active) return (named, null);
                }

                return (null, null);
            }

            default:
                return (null, null);
        }
    }

    private Task<Guid?> ResolveManagerUserIdAsync(Guid? requesterEmployeeId, CancellationToken cancellationToken)
    {
        if (requesterEmployeeId is null) return Task.FromResult<Guid?>(null);

        return (
            from e in _db.Employees.AsNoTracking()
            where e.Id == requesterEmployeeId
            from m in _db.Employees.AsNoTracking().Where(x => x.Id == e.ManagerId)
            from u in _db.UserAccounts.AsNoTracking()
                .Where(u => u.EmployeeId == m.Id && u.Status == UserStatus.Active)
            select (Guid?)u.Id
        ).FirstOrDefaultAsync(cancellationToken);
    }

    private Task<Guid?> ResolveRoleUserIdAsync(string roleName, CancellationToken cancellationToken)
    {
        var needle = roleName.Trim().ToLower();
        return (
            from u in _db.UserAccounts.AsNoTracking()
            where u.Status == UserStatus.Active
            where u.UserRoles.Any(r => r.Role!.Name.ToLower() == needle)
            orderby u.Employee != null ? u.Employee.EmployeeNumber : u.UserName
            select (Guid?)u.Id
        ).FirstOrDefaultAsync(cancellationToken);
    }

    private static DateTime? ComputeDueAt(DateTime now, int? slaDays)
        => slaDays is null or <= 0 ? null : now.Date.AddDays(slaDays.Value);

    private async Task NotifyAssigneeAsync(ApprovalTask task, CancellationToken cancellationToken)
    {
        if (task.CurrentAssigneeUserId is null) return;

        var notifyUserId = task.DelegatedToUserId ?? task.CurrentAssigneeUserId;
        var employeeId = await (
                from u in _db.UserAccounts.AsNoTracking()
                where u.Id == notifyUserId
                select (Guid?)u.EmployeeId)
            .FirstOrDefaultAsync(cancellationToken);

        await PublishAsync(notifyUserId, employeeId, "ApprovalAssigned",
            $"Approval required: {task.Title}",
            task.Amount is null
                ? $"{task.RequesterName ?? "A colleague"} submitted a {task.RequestType} request that needs your approval."
                : $"{task.RequesterName ?? "A colleague"} submitted a {task.RequestType} claim of {task.Amount:0.##} {task.Currency ?? ""} that needs your approval.",
            DeepLink(task.Id), cancellationToken);
    }

    private Task PublishAsync(Guid? userId, Guid? employeeId, string type, string title, string body,
        string deepLink, CancellationToken cancellationToken)
    {
        if (userId is null) return Task.CompletedTask;
        return _notifications.PublishAsync(
            new NotificationMessage(userId, type, title, body, deepLink, Category: "Approvals"),
            cancellationToken);
    }

    private async Task<string> DisplayNameAsync(Guid? userId, string? fallback, CancellationToken cancellationToken)
    {
        if (userId is null) return fallback ?? "System";
        var name = await (
                from u in _db.UserAccounts.AsNoTracking()
                where u.Id == userId
                select (string?)(u.Employee != null ? u.Employee.DisplayName : u.UserName))
            .FirstOrDefaultAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(name) ? fallback ?? "Unknown user" : name!;
    }

    /// <summary>Single SaveChanges per unit of work; maps optimistic-concurrency failures to 409.</summary>
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            _logger.LogWarning(exception, "Optimistic concurrency conflict while updating an approval.");
            throw new ConflictException("This approval was updated by someone else. Refresh the task and try again.");
        }
    }

    private static string DeepLink(Guid taskId) => $"hrms://approvals/{taskId}";
}
