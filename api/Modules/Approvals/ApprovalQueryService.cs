using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.CurrentUser;
using Wintech.Hrms.Api.Common.Paging;
using Wintech.Hrms.Api.Common.Registration;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Approvals;
using Wintech.Hrms.Api.Security;

namespace Wintech.Hrms.Api.Modules.Approvals;

/// <summary>Read side of the approvals centre (pending/mine/stats/detail/workflows).</summary>
public interface IApprovalQueryService : IAppService
{
    Task<PagedResult<PendingApprovalItem>> GetPendingAsync(PageRequest page, CancellationToken cancellationToken = default);
    Task<PagedResult<MyApprovalItem>> GetMineAsync(PageRequest page, CancellationToken cancellationToken = default);
    Task<ApprovalStatsDto> GetStatsAsync(CancellationToken cancellationToken = default);
    /// <summary>Returns null when the task does not exist or is outside the caller's scope (404, no existence leak).</summary>
    Task<ApprovalDetailDto?> GetDetailAsync(Guid taskId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkflowDto>> GetWorkflowsAsync(CancellationToken cancellationToken = default);
}

public sealed class ApprovalQueryService : IApprovalQueryService
{
    private const string Pending = nameof(ApprovalStatus.Pending);

    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public ApprovalQueryService(AppDbContext db, ICurrentUser currentUser, IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<PagedResult<PendingApprovalItem>> GetPendingAsync(PageRequest page,
        CancellationToken cancellationToken = default)
    {
        var me = _currentUser.RequireUserId();
        var now = _clock.UtcNow;
        var canManageAll = _currentUser.HasPermission(Permissions.All);

        var query =
            from t in _db.ApprovalTasks.AsNoTracking()
            where t.Status == Pending
                  && (canManageAll || t.CurrentAssigneeUserId == me ||
                      (t.DelegatedToUserId == me &&
                       (t.DelegationExpiresAt == null || t.DelegationExpiresAt > now)))
            orderby t.DueAt == null, t.DueAt, t.CreatedAt descending
            select new
            {
                t.Id,
                t.RequestType,
                t.RequestId,
                t.Title,
                t.RequesterName,
                t.RequesterEmployeeId,
                RequestedAt = t.CreatedAt,
                StepName = t.CurrentStepName,
                t.DueAt,
                t.Amount,
                t.Currency,
                t.Status,
                EmployeeNumber = t.RequesterEmployeeId == null
                    ? null
                    : _db.Employees.Where(e => e.Id == t.RequesterEmployeeId)
                        .Select(e => e.EmployeeNumber)
                        .FirstOrDefault()
            };

        return await query.ToPagedAsync(page, row => new PendingApprovalItem(
            row.Id,
            row.RequestType,
            row.RequestId,
            row.Title,
            row.RequesterName ?? string.Empty,
            row.EmployeeNumber,
            row.RequestedAt,
            row.StepName,
            row.DueAt,
            row.Amount,
            row.Currency,
            row.Status,
            $"hrms://approvals/{row.Id}"), cancellationToken);
    }

    public async Task<PagedResult<MyApprovalItem>> GetMineAsync(PageRequest page,
        CancellationToken cancellationToken = default)
    {
        var me = _currentUser.RequireUserId();

        var query =
            from t in _db.ApprovalTasks.AsNoTracking()
            where t.RequesterUserId == me
            orderby t.UpdatedAt descending
            select new
            {
                t.Id,
                t.RequestType,
                t.RequestId,
                t.Title,
                t.Status,
                CurrentStep = t.CurrentStepName,
                ApproverName = t.CurrentAssigneeUserId == null
                    ? null
                    : _db.UserAccounts.Where(u => u.Id == t.CurrentAssigneeUserId)
                        .Select(u => u.Employee != null ? u.Employee.DisplayName : u.UserName)
                        .FirstOrDefault(),
                t.UpdatedAt
            };

        return await query.ToPagedAsync(page, row => new MyApprovalItem(
            row.Id,
            row.RequestType,
            row.RequestId,
            row.Title,
            row.Status,
            row.CurrentStep,
            row.ApproverName,
            row.UpdatedAt), cancellationToken);
    }

    public async Task<ApprovalStatsDto> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var me = _currentUser.RequireUserId();
        var now = _clock.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var awaitingMe =
            from t in _db.ApprovalTasks.AsNoTracking()
            where t.Status == Pending
                  && (t.CurrentAssigneeUserId == me ||
                      (t.DelegatedToUserId == me &&
                       (t.DelegationExpiresAt == null || t.DelegationExpiresAt > now)))
            select t;

        var pending = await awaitingMe.CountAsync(cancellationToken);
        var overdue = await awaitingMe.CountAsync(t => t.DueAt != null && t.DueAt < now, cancellationToken);
        var completedThisMonth = await _db.ApprovalActions.AsNoTracking()
            .CountAsync(a => a.ActorUserId == me &&
                             (a.Action == ApprovalActionType.Approve ||
                              a.Action == ApprovalActionType.Reject ||
                              a.Action == ApprovalActionType.Return) &&
                             a.At >= monthStart, cancellationToken);

        return new ApprovalStatsDto(pending, overdue, completedThisMonth);
    }

    public async Task<ApprovalDetailDto?> GetDetailAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        var me = _currentUser.RequireUserId();
        var now = _clock.UtcNow;

        var task = await _db.ApprovalTasks.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null) return null;

        var isRequester = task.RequesterUserId == me;
        var isPendingDelegate = task.DelegatedToUserId == me &&
                                (task.DelegationExpiresAt == null || task.DelegationExpiresAt > now);
        var isVisible = isRequester ||
                        (task.Status == Pending && (task.CurrentAssigneeUserId == me || isPendingDelegate)) ||
                        await _db.ApprovalActions.AsNoTracking()
                            .AnyAsync(a => a.TaskId == taskId && a.ActorUserId == me, cancellationToken) ||
                        _currentUser.HasPermission(Permissions.AdminConfig);
        if (!isVisible) return null;

        var history = await (
                from a in _db.ApprovalActions.AsNoTracking()
                where a.TaskId == taskId
                orderby a.At, a.StepNumber
                select new ApprovalHistoryItem(a.StepNumber, a.Action, a.ActorName, a.Comment, a.At))
            .ToListAsync(cancellationToken);

        var canDecide = task.Status == Pending &&
                        task.RequesterUserId != me &&
                        (task.CurrentAssigneeUserId == me || isPendingDelegate ||
                         _currentUser.HasPermission(Permissions.All));

        return new ApprovalDetailDto
        {
            TaskId = task.Id,
            RequestType = task.RequestType,
            RequestId = task.RequestId,
            Title = task.Title,
            Status = task.Status,
            CurrentStep = task.CurrentStepName,
            DueAt = task.DueAt,
            RequesterName = task.RequesterName,
            CanDecide = canDecide,
            History = history
        };
    }

    public async Task<IReadOnlyList<WorkflowDto>> GetWorkflowsAsync(CancellationToken cancellationToken = default)
    {
        var definitions = await _db.ApprovalWorkflowDefinitions.AsNoTracking()
            .Include(w => w.Steps)
            .OrderBy(w => w.Code)
            .ThenByDescending(w => w.Version)
            .ToListAsync(cancellationToken);

        return definitions.Select(w => new WorkflowDto(
            w.Id,
            w.Code,
            w.Name,
            w.Version,
            w.IsActive,
            w.Steps.OrderBy(s => s.StepNumber)
                .Select(s => new WorkflowStepDto(s.StepNumber, s.Name, s.ApproverType, s.ApproverValue, s.SLADays))
                .ToList())).ToList();
    }
}
