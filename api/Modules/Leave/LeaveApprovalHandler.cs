using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Auditing;
using Wintech.Hrms.Api.Common.Notifications;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Approvals;
using Wintech.Hrms.Api.Domain.Leave;

namespace Wintech.Hrms.Api.Modules.Leave;

/// <summary>
/// Updates the leave request exactly once when its workflow finishes (UAT-004) and moves the
/// balance units between Pending and Used, or releases them on reject/return/cancel
/// (FR-EMP-040/046, FR-ADM-053). Every transition is audited and the requester notified.
/// </summary>
public sealed class LeaveApprovalHandler : IApprovalHandler
{
    private readonly AppDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly INotificationPublisher _notifications;

    public LeaveApprovalHandler(AppDbContext db, IClock clock, IAuditService audit,
        INotificationPublisher notifications)
    {
        _db = db;
        _clock = clock;
        _audit = audit;
        _notifications = notifications;
    }

    public string RequestType => "Leave";

    public async Task OnCompletedAsync(ApprovalTask task, ApprovalActionType finalAction, string? comment,
        CancellationToken cancellationToken = default)
    {
        var request = await _db.Set<LeaveRequest>()
            .FirstOrDefaultAsync(r => r.Id == task.RequestId, cancellationToken);
        if (request is null || request.Status != LeaveRequestStatus.Pending) return; // exactly once (UAT-004)

        var previous = request.Status;
        request.Status = finalAction switch
        {
            ApprovalActionType.Approve => LeaveRequestStatus.Approved,
            ApprovalActionType.Reject => LeaveRequestStatus.Rejected,
            _ => LeaveRequestStatus.Returned
        };
        request.DecidedAt = _clock.UtcNow;
        if (request.Status == LeaveRequestStatus.Rejected) request.RejectionReason = comment;

        await AdjustBalanceAsync(request, previous, request.Status, cancellationToken);

        _audit.Add($"Leave.Request.{request.Status}", nameof(LeaveRequest), request.Id.ToString(), details: new
        {
            request.StartDate,
            request.EndDate,
            units = request.Units,
            status = request.Status.ToString(),
            comment
        });

        await _notifications.PublishAsync(new NotificationMessage(
            task.RequesterUserId, $"LeaveRequest{request.Status}",
            $"Leave request {request.Status.ToString().ToLowerInvariant()}",
            BuildDecisionBody(request, comment),
            $"/leave/requests/{request.Id}", "Leave", EmployeeId: request.EmployeeId),
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task OnCancelledAsync(ApprovalTask task, string? reason, CancellationToken cancellationToken = default)
    {
        var request = await _db.Set<LeaveRequest>()
            .FirstOrDefaultAsync(r => r.Id == task.RequestId, cancellationToken);
        if (request is null || request.Status != LeaveRequestStatus.Pending) return;

        var previous = request.Status;
        request.Status = LeaveRequestStatus.Cancelled;
        request.DecidedAt = _clock.UtcNow;
        request.RejectionReason = reason;

        await AdjustBalanceAsync(request, previous, request.Status, cancellationToken);

        _audit.Add("Leave.Request.Cancelled", nameof(LeaveRequest), request.Id.ToString(), details: new
        {
            request.StartDate,
            request.EndDate,
            units = request.Units,
            reason
        });

        await _notifications.PublishAsync(new NotificationMessage(
            task.RequesterUserId, "LeaveRequestCancelled",
            "Leave request cancelled",
            $"Your leave request for {request.StartDate:yyyy-MM-dd} to {request.EndDate:yyyy-MM-dd} was cancelled." +
            (string.IsNullOrWhiteSpace(reason) ? string.Empty : $" Reason: {reason}"),
            $"/leave/requests/{request.Id}", "Leave", EmployeeId: request.EmployeeId),
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Pending -> Used on approval; Pending -> released on rejection, return or cancellation.
    /// </summary>
    private async Task AdjustBalanceAsync(LeaveRequest request, LeaveRequestStatus previous,
        LeaveRequestStatus current, CancellationToken cancellationToken)
    {
        if (previous != LeaveRequestStatus.Pending) return;
        if (current is not (LeaveRequestStatus.Approved or LeaveRequestStatus.Rejected
            or LeaveRequestStatus.Returned or LeaveRequestStatus.Cancelled))
            return;

        var balance = await _db.Set<LeaveBalance>().FirstOrDefaultAsync(
            b => b.EmployeeId == request.EmployeeId && b.LeaveTypeId == request.LeaveTypeId
                 && b.Year == request.StartDate.Year, cancellationToken);
        if (balance is null) return;

        balance.Pending = Math.Max(0, balance.Pending - request.Units);
        if (current == LeaveRequestStatus.Approved)
            balance.Used += request.Units;
    }

    private static string BuildDecisionBody(LeaveRequest request, string? comment)
    {
        var period = $"{request.StartDate:yyyy-MM-dd} to {request.EndDate:yyyy-MM-dd}";
        var body = request.Status switch
        {
            LeaveRequestStatus.Approved => $"Your leave ({period}) was approved.",
            LeaveRequestStatus.Rejected => $"Your leave ({period}) was rejected.",
            LeaveRequestStatus.Returned => $"Your leave ({period}) was returned for changes.",
            LeaveRequestStatus.Cancelled => $"Your leave ({period}) was cancelled.",
            _ => $"Your leave ({period}) is now {request.Status}."
        };
        return string.IsNullOrWhiteSpace(comment) ? body : $"{body} Comment: {comment}";
    }
}
