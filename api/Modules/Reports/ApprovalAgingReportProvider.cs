using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Reporting;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Approvals;

namespace Wintech.Hrms.Api.Modules.Reports;

/// <summary>One in-flight approval in the aging report (contract §18).</summary>
public sealed record ApprovalAgingRow(
    string RequestType,
    string? RequesterName,
    string? ApproverName,
    string StepName,
    int AgeDays,
    DateTime? DueAt,
    bool Overdue);

/// <summary>
/// Approval aging (BRD section 13): every still-pending approval with the step it sits on, how long
/// it has been waiting, its SLA due date and whether that due date has passed. Rows are built from
/// <c>ApprovalTask</c> (state) joined with <c>ApprovalAction</c> (last actor fallback) and the
/// assignee's identity. Department/location filters scope through the requester's employee record.
/// </summary>
public sealed class ApprovalAgingReportProvider : IReportDataProvider
{
    private readonly AppDbContext _db;
    private readonly IClock _clock;

    public ApprovalAgingReportProvider(AppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public string Report => "approval-aging";

    public async Task<IReadOnlyList<object>> GetRowsAsync(ReportFilter filter, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var windowStart = filter.From.ToDateTime(TimeOnly.MinValue);
        var windowEndExclusive = filter.To.AddDays(1).ToDateTime(TimeOnly.MinValue);

        IQueryable<ApprovalTask> query = _db.ApprovalTasks.AsNoTracking()
            .Where(t => t.Status == nameof(ApprovalStatus.Pending) &&
                        t.CreatedAt >= windowStart && t.CreatedAt < windowEndExclusive);

        if (filter.DepartmentId.HasValue || filter.LocationId.HasValue)
        {
            var employeeQuery = _db.Employees.AsNoTracking();
            if (filter.DepartmentId is { } departmentId)
                employeeQuery = employeeQuery.Where(e => e.DepartmentId == departmentId);
            if (filter.LocationId is { } locationId)
                employeeQuery = employeeQuery.Where(e => e.LocationId == locationId);

            var employeeIds = await employeeQuery.Select(e => (Guid?)e.Id).ToArrayAsync(cancellationToken);
            query = query.Where(t => t.RequesterEmployeeId != null && employeeIds.Contains(t.RequesterEmployeeId));
        }

        var tasks = await query
            .OrderBy(t => t.CreatedAt)
            .Select(t => new
            {
                t.RequestType,
                t.RequesterName,
                t.CurrentStepName,
                t.CreatedAt,
                t.DueAt,
                t.CurrentAssigneeUserId,
                t.CurrentAssigneeRole,
                LastActionActor = t.Actions.OrderByDescending(a => a.At).Select(a => a.ActorName).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var assigneeIds = tasks
            .Where(t => t.CurrentAssigneeUserId.HasValue)
            .Select(t => t.CurrentAssigneeUserId.GetValueOrDefault())
            .Distinct()
            .ToArray();

        var assigneeNames = assigneeIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await _db.UserAccounts.AsNoTracking()
                .Where(u => assigneeIds.Contains(u.Id))
                .Select(u => new { u.Id, Name = u.Employee != null ? u.Employee.DisplayName : u.UserName })
                .ToDictionaryAsync(u => u.Id, u => u.Name, cancellationToken);

        return tasks.Select(t =>
        {
            string? approverName = null;
            if (t.CurrentAssigneeUserId is { } assigneeId && assigneeNames.TryGetValue(assigneeId, out var resolved) &&
                !string.IsNullOrWhiteSpace(resolved))
            {
                approverName = resolved;
            }
            else if (!string.IsNullOrWhiteSpace(t.CurrentAssigneeRole))
            {
                approverName = t.CurrentAssigneeRole;
            }
            else if (!string.IsNullOrWhiteSpace(t.LastActionActor))
            {
                approverName = t.LastActionActor;
            }

            var ageDays = (int)Math.Floor(Math.Max(0, (now - t.CreatedAt).TotalDays));
            return (object)new ApprovalAgingRow(
                RequestType: t.RequestType,
                RequesterName: t.RequesterName,
                ApproverName: approverName,
                StepName: t.CurrentStepName,
                AgeDays: ageDays,
                DueAt: t.DueAt,
                Overdue: t.DueAt is { } dueAt && dueAt < now);
        }).ToList();
    }
}
