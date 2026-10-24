using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Auditing;
using Wintech.Hrms.Api.Common.Configuration;
using Wintech.Hrms.Api.Common.CurrentUser;
using Wintech.Hrms.Api.Common.ErrorHandling;
using Wintech.Hrms.Api.Common.Notifications;
using Wintech.Hrms.Api.Common.Paging;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Approvals;
using Wintech.Hrms.Api.Domain.Attendance;
using Wintech.Hrms.Api.Security;

namespace Wintech.Hrms.Api.Modules.Attendance;

public sealed class AttendanceCorrectionService : IAttendanceCorrectionService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly IApprovalService _approvalService;
    private readonly INotificationPublisher _notifications;
    private readonly SecurityOptions _securityOptions;

    public AttendanceCorrectionService(AppDbContext db, ICurrentUser currentUser, IClock clock,
        IAuditService audit, IApprovalService approvalService, INotificationPublisher notifications,
        SecurityOptions securityOptions)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
        _approvalService = approvalService;
        _notifications = notifications;
        _securityOptions = securityOptions;
    }

    public async Task<AttendanceCorrectionCreatedDto> CreateAsync(AttendanceCorrectionCreateDto request,
        CancellationToken cancellationToken = default)
    {
        var employeeId = RequireEmployeeId();
        var userId = _currentUser.RequireUserId();

        if (!Enum.TryParse<AttendanceCorrectionType>(request.Type, true, out var type))
            throw new RequestValidationException(errors: new()
            {
                ["type"] = ["Expected MissingPunch, IncorrectCheckIn, IncorrectCheckOut, IncorrectTimes, ShiftCorrection, Overtime or Other."]
            });

        var today = AttendanceTimeHelper.DateLocal(_clock.UtcNow);
        if (request.WorkDate > today)
            throw new BusinessRuleException(errors: new()
            {
                ["workDate"] = ["Corrections can only be submitted for past dates."]
            });

        var windowDays = Math.Max(1, _securityOptions.AttendanceCorrectionWindowDays);
        var earliest = today.AddDays(-windowDays);
        if (request.WorkDate < earliest)
            throw new BusinessRuleException(
                $"Corrections can only be submitted within the last {windowDays} day(s).",
                new() { ["workDate"] = [$"The earliest submittable date is {earliest:yyyy-MM-dd}."] });

        if (request.ProposedCheckIn is null && request.ProposedCheckOut is null)
            throw new RequestValidationException(errors: new()
            {
                ["proposedCheckIn"] = ["Provide at least one of proposedCheckIn or proposedCheckOut."],
                ["proposedCheckOut"] = ["Provide at least one of proposedCheckIn or proposedCheckOut."]
            });

        var hasOpen = await _db.Set<AttendanceCorrectionRequest>().AsNoTracking()
            .AnyAsync(c => c.EmployeeId == employeeId && c.WorkDate == request.WorkDate
                           && c.Status == AttendanceCorrectionStatus.Pending, cancellationToken);
        if (hasOpen)
            throw new ConflictException("A pending correction already exists for that date. Wait for it to be decided before submitting another.");

        var record = await _db.Set<AttendanceDayRecord>().AsNoTracking()
            .FirstOrDefaultAsync(r => r.EmployeeId == employeeId && r.WorkDate == request.WorkDate, cancellationToken);

        var correction = new AttendanceCorrectionRequest
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            WorkDate = request.WorkDate,
            Type = type,
            ProposedCheckIn = request.ProposedCheckIn,
            ProposedCheckOut = request.ProposedCheckOut,
            OriginalCheckIn = record?.CheckIn,
            OriginalCheckOut = record?.CheckOut,
            Reason = request.Reason.Trim(),
            AttachmentUrl = request.AttachmentUrl,
            Status = AttendanceCorrectionStatus.Pending,
            SubmittedAt = _clock.UtcNow,
            CreatedByUserId = userId
        };
        _db.Set<AttendanceCorrectionRequest>().Add(correction);
        await _db.SaveChangesAsync(cancellationToken);

        var title = $"{type} correction for {request.WorkDate:yyyy-MM-dd} ({_currentUser.DisplayName})";
        var task = await _approvalService.StartAsync(new ApprovalStartRequest(
            "AttendanceCorrection", correction.Id, title, userId, employeeId, _currentUser.DisplayName),
            cancellationToken);

        correction.ApprovalTaskId = task.Id;
        _audit.Add("Attendance.Correction.Submitted", nameof(AttendanceCorrectionRequest), correction.Id.ToString(),
            details: new
            {
                workDate = correction.WorkDate,
                type = correction.Type.ToString(),
                correction.ProposedCheckIn,
                correction.ProposedCheckOut,
                originalCheckIn = correction.OriginalCheckIn,
                originalCheckOut = correction.OriginalCheckOut,
                correction.Reason,
                approvalTaskId = task.Id
            });
        await _db.SaveChangesAsync(cancellationToken);

        return new AttendanceCorrectionCreatedDto(correction.Id, correction.Status, task.Id);
    }

    public async Task<PagedResult<AttendanceCorrectionListItem>> ListAsync(string? status, string? month,
        Guid? employeeId, PageRequest page, CancellationToken cancellationToken = default)
    {
        var targetId = await ResolveTargetEmployeeIdAsync(employeeId, cancellationToken);

        AttendanceCorrectionStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<AttendanceCorrectionStatus>(status, true, out var parsed))
                throw new RequestValidationException(errors: new() { ["status"] = ["Unknown status value."] });
            statusFilter = parsed;
        }

        DateOnly? monthFilter = null;
        if (!string.IsNullOrWhiteSpace(month))
            monthFilter = ParseMonth(month);

        var query = _db.Set<AttendanceCorrectionRequest>().AsNoTracking()
            .Where(c => c.EmployeeId == targetId);

        if (statusFilter is not null)
        {
            var value = statusFilter.Value;
            query = query.Where(c => c.Status == value);
        }

        if (monthFilter is { } monthStart)
        {
            var from = AttendanceTimeHelper.FirstDayOfMonth(monthStart);
            var to = AttendanceTimeHelper.LastDayOfMonth(monthStart);
            query = query.Where(c => c.WorkDate >= from && c.WorkDate <= to);
        }

        var paged = await query
            .OrderByDescending(c => c.WorkDate)
            .ThenByDescending(c => c.SubmittedAt)
            .ToPagedAsync(page, c => new
            {
                c.Id, c.WorkDate, c.Type, c.ProposedCheckIn, c.ProposedCheckOut,
                c.OriginalCheckIn, c.OriginalCheckOut, c.Reason, c.Status, c.SubmittedAt, c.ApprovalTaskId
            }, cancellationToken);

        var approverNames = await ResolveApproverNamesAsync(paged.Items.Select(i => i.ApprovalTaskId), cancellationToken);

        var items = paged.Items.Select(i => new AttendanceCorrectionListItem(
            i.Id, i.WorkDate, i.Type, i.ProposedCheckIn, i.ProposedCheckOut,
            i.OriginalCheckIn, i.OriginalCheckOut, i.Reason, i.Status, i.SubmittedAt,
            approverNames.GetValueOrDefault(i.ApprovalTaskId ?? Guid.Empty), i.ApprovalTaskId)).ToList();

        return PagedResult<AttendanceCorrectionListItem>.Create(items, paged.Page, paged.PageSize, paged.Total);
    }

    public async Task<AttendanceCorrectionDetail> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var correction = await _db.Set<AttendanceCorrectionRequest>().AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (correction is null ||
            (correction.EmployeeId != _currentUser.EmployeeId && !CanViewTeam()))
            throw new NotFoundException();

        var approverNames = await ResolveApproverNamesAsync([correction.ApprovalTaskId], cancellationToken);
        var history = await BuildDecisionHistoryAsync(correction.ApprovalTaskId, includeStep: false, cancellationToken);

        return new AttendanceCorrectionDetail(
            correction.Id, correction.WorkDate, correction.Type,
            correction.ProposedCheckIn, correction.ProposedCheckOut,
            correction.OriginalCheckIn, correction.OriginalCheckOut,
            correction.Reason, correction.AttachmentUrl, correction.Status,
            correction.SubmittedAt, correction.DecidedAt,
            approverNames.GetValueOrDefault(correction.ApprovalTaskId ?? Guid.Empty),
            correction.ApprovalTaskId, history);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Keyed by approval task id: pending -> current assignee; completed -> last decision actor.</summary>
    private async Task<IReadOnlyDictionary<Guid, string>> ResolveApproverNamesAsync(
        IEnumerable<Guid?> taskIds, CancellationToken cancellationToken)
    {
        var ids = taskIds.Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        var result = new Dictionary<Guid, string>();
        if (ids.Count == 0) return result;

        var tasks = await _db.Set<ApprovalTask>().AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .ToListAsync(cancellationToken);

        var assigneeIds = tasks.Where(t => t.CurrentAssigneeUserId is not null)
            .Select(t => t.CurrentAssigneeUserId!.Value).Distinct().ToList();
        var assigneeNames = new Dictionary<Guid, string>();
        if (assigneeIds.Count > 0)
        {
            var users = await _db.UserAccounts.AsNoTracking()
                .Where(u => assigneeIds.Contains(u.Id))
                .Select(u => new { u.Id, Name = u.Employee != null ? u.Employee.DisplayName : u.UserName })
                .ToListAsync(cancellationToken);
            assigneeNames = users.ToDictionary(u => u.Id, u => u.Name);
        }

        var completedIds = tasks.Where(t => t.Status != nameof(ApprovalStatus.Pending)).Select(t => t.Id).ToList();
        var lastActors = new Dictionary<Guid, string>();
        if (completedIds.Count > 0)
        {
            var actions = await _db.Set<ApprovalAction>().AsNoTracking()
                .Where(a => completedIds.Contains(a.TaskId))
                .OrderBy(a => a.At)
                .ToListAsync(cancellationToken);
            foreach (var action in actions)
            {
                if (action.Action is ApprovalActionType.Approve or ApprovalActionType.Reject or ApprovalActionType.Return)
                    lastActors[action.TaskId] = action.ActorName;
            }
        }

        foreach (var task in tasks)
        {
            var name = task.Status == nameof(ApprovalStatus.Pending)
                ? (task.CurrentAssigneeUserId is { } assigneeId ? assigneeNames.GetValueOrDefault(assigneeId) : null)
                : lastActors.GetValueOrDefault(task.Id);
            if (!string.IsNullOrWhiteSpace(name)) result[task.Id] = name;
        }

        return result;
    }

    internal async Task<IReadOnlyList<DecisionHistoryEntry>> BuildDecisionHistoryAsync(
        Guid? taskId, bool includeStep, CancellationToken cancellationToken)
    {
        if (taskId is null) return [];
        var actions = await _db.Set<ApprovalAction>().AsNoTracking()
            .Where(a => a.TaskId == taskId.Value)
            .OrderBy(a => a.At)
            .ToListAsync(cancellationToken);

        var stepNames = new Dictionary<int, string>();
        if (includeStep)
        {
            var task = await _db.Set<ApprovalTask>().AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == taskId.Value, cancellationToken);
            if (task?.WorkflowDefinitionId is { } workflowId)
            {
                var steps = await _db.Set<ApprovalWorkflowStep>().AsNoTracking()
                    .Where(s => s.WorkflowId == workflowId)
                    .ToListAsync(cancellationToken);
                foreach (var step in steps) stepNames[step.StepNumber] = step.Name;
            }
        }

        return actions.Select(a => new DecisionHistoryEntry(
            includeStep ? stepNames.GetValueOrDefault(a.StepNumber, $"Step {a.StepNumber}") : null,
            a.Action.ToString(), a.ActorName, a.Comment, a.At)).ToList();
    }

    private bool CanViewTeam() =>
        _currentUser.HasPermission(Permissions.AttendanceViewTeam) ||
        _currentUser.HasPermission(Permissions.AttendanceManage);

    private Guid RequireEmployeeId() =>
        _currentUser.EmployeeId
        ?? throw new ForbiddenException("Your user account is not linked to an employee record.");

    private async Task<Guid> ResolveTargetEmployeeIdAsync(Guid? requestedEmployeeId, CancellationToken cancellationToken)
    {
        var mine = RequireEmployeeId();
        if (requestedEmployeeId is null || requestedEmployeeId == mine) return mine;
        if (!CanViewTeam()) throw new NotFoundException(); // scope: no existence leak
        var exists = await _db.Employees.AsNoTracking().AnyAsync(e => e.Id == requestedEmployeeId, cancellationToken);
        if (!exists) throw new NotFoundException();
        return requestedEmployeeId.Value;
    }

    private static DateOnly ParseMonth(string? month)
    {
        if (DateOnly.TryParseExact(month!.Trim(), "yyyy-MM", out var parsed))
            return AttendanceTimeHelper.FirstDayOfMonth(parsed);
        throw new RequestValidationException(errors: new() { ["month"] = ["Expected format YYYY-MM (e.g. 2026-10)."] });
    }
}
