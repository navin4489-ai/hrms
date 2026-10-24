using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Auditing;
using Wintech.Hrms.Api.Common.Notifications;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Approvals;
using Wintech.Hrms.Api.Domain.Expenses;

namespace Wintech.Hrms.Api.Modules.Expenses;

/// <summary>
/// Applies completed "Expense" workflows onto the claim exactly once (UAT-004):
/// approve -> Approved, reject -> Rejected with the reason (BR-007), and the matching
/// notifications/audit entries (FR-EMP-084).
/// </summary>
public sealed class ExpenseApprovalHandler : IApprovalHandler
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly INotificationPublisher _notifications;
    private readonly IClock _clock;

    public ExpenseApprovalHandler(AppDbContext db, IAuditService audit,
        INotificationPublisher notifications, IClock clock)
    {
        _db = db;
        _audit = audit;
        _notifications = notifications;
        _clock = clock;
    }

    public string RequestType => "Expense";

    public async Task OnCompletedAsync(ApprovalTask task, ApprovalActionType finalAction, string? comment,
        CancellationToken cancellationToken = default)
    {
        var claim = await _db.Set<ExpenseClaim>().FirstOrDefaultAsync(
            c => c.Id == task.RequestId || (c.ApprovalTaskId != null && c.ApprovalTaskId == task.Id), cancellationToken);
        if (claim is null) return;

        switch (finalAction)
        {
            case ApprovalActionType.Approve:
                claim.Status = ExpenseClaimStatus.Approved;
                claim.ApprovedAt = _clock.UtcNow;
                claim.RejectionReason = null;
                _audit.Add("Expense.Approved", nameof(ExpenseClaim), claim.Id.ToString(),
                    details: new { amount = claim.Amount, claim.Currency, approver = task.CurrentAssigneeUserId });
                await NotifyAsync(claim, "Expense claim approved",
                    $"Your claim ({claim.Category} {claim.Amount:N0} {claim.Currency}) has been approved.");
                break;

            case ApprovalActionType.Reject:
                claim.Status = ExpenseClaimStatus.Rejected;
                claim.RejectionReason = comment;
                _audit.Add("Expense.Rejected", nameof(ExpenseClaim), claim.Id.ToString(),
                    details: new { reason = comment });
                await NotifyAsync(claim, "Expense claim rejected",
                    $"Your claim ({claim.Category} {claim.Amount:N0} {claim.Currency}) was rejected.{ReasonSuffix(comment)}");
                break;

            case ApprovalActionType.Return:
                claim.Status = ExpenseClaimStatus.Returned;
                claim.RejectionReason = comment;
                _audit.Add("Expense.Returned", nameof(ExpenseClaim), claim.Id.ToString(),
                    details: new { reason = comment });
                await NotifyAsync(claim, "Expense claim returned",
                    $"Your claim ({claim.Category} {claim.Amount:N0} {claim.Currency}) was returned for correction.{ReasonSuffix(comment)}");
                break;

            default:
                return; // intermediate steps (delegate/escalate) do not change the claim state
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task OnCancelledAsync(ApprovalTask task, string? reason, CancellationToken cancellationToken = default)
    {
        var claim = await _db.Set<ExpenseClaim>().FirstOrDefaultAsync(
            c => c.Id == task.RequestId || (c.ApprovalTaskId != null && c.ApprovalTaskId == task.Id), cancellationToken);
        if (claim is null) return;

        claim.Status = ExpenseClaimStatus.Cancelled;
        _audit.Add("Expense.Cancelled", nameof(ExpenseClaim), claim.Id.ToString(), details: new { reason });
        await NotifyAsync(claim, "Expense claim cancelled",
            $"Your claim ({claim.Category} {claim.Amount:N0} {claim.Currency}) was cancelled.");
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task NotifyAsync(ExpenseClaim claim, string title, string body)
    {
        var userId = await _db.UserAccounts.AsNoTracking()
            .Where(u => u.EmployeeId == claim.EmployeeId && u.Status == Domain.Identity.UserStatus.Active)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync();
        if (userId is null) return;

        await _notifications.PublishAsync(new NotificationMessage(
            userId, "Expense", title, body, DeepLink: "/expenses", Category: "Expense",
            EmployeeId: claim.EmployeeId));
    }

    private static string ReasonSuffix(string? comment) =>
        string.IsNullOrWhiteSpace(comment) ? string.Empty : $" Reason: {comment}";
}

/// <summary>Applies completed "Travel" workflows onto the travel request (FR-EMP-083).</summary>
public sealed class TravelApprovalHandler : IApprovalHandler
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly INotificationPublisher _notifications;
    private readonly IClock _clock;

    public TravelApprovalHandler(AppDbContext db, IAuditService audit,
        INotificationPublisher notifications, IClock clock)
    {
        _db = db;
        _audit = audit;
        _notifications = notifications;
        _clock = clock;
    }

    public string RequestType => "Travel";

    public async Task OnCompletedAsync(ApprovalTask task, ApprovalActionType finalAction, string? comment,
        CancellationToken cancellationToken = default)
    {
        var request = await _db.Set<TravelRequest>().FirstOrDefaultAsync(
            t => t.Id == task.RequestId || (t.ApprovalTaskId != null && t.ApprovalTaskId == task.Id), cancellationToken);
        if (request is null) return;

        switch (finalAction)
        {
            case ApprovalActionType.Approve:
                request.Status = TravelRequestStatus.Approved;
                request.DecidedAt = _clock.UtcNow;
                request.RejectionReason = null;
                _audit.Add("Travel.Approved", nameof(TravelRequest), request.Id.ToString(),
                    details: new { request.Destination, request.EstimatedCost, request.Currency });
                await NotifyAsync(request, "Travel request approved",
                    $"Your trip to {request.Destination} has been approved.");
                break;

            case ApprovalActionType.Reject:
                request.Status = TravelRequestStatus.Rejected;
                request.DecidedAt = _clock.UtcNow;
                request.RejectionReason = comment;
                _audit.Add("Travel.Rejected", nameof(TravelRequest), request.Id.ToString(),
                    details: new { reason = comment });
                await NotifyAsync(request, "Travel request rejected",
                    $"Your trip to {request.Destination} was rejected.{(string.IsNullOrWhiteSpace(comment) ? string.Empty : $" Reason: {comment}")}");
                break;

            default:
                return;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task OnCancelledAsync(ApprovalTask task, string? reason, CancellationToken cancellationToken = default)
    {
        var request = await _db.Set<TravelRequest>().FirstOrDefaultAsync(
            t => t.Id == task.RequestId || (t.ApprovalTaskId != null && t.ApprovalTaskId == task.Id), cancellationToken);
        if (request is null) return;

        request.Status = TravelRequestStatus.Cancelled;
        request.DecidedAt = _clock.UtcNow;
        _audit.Add("Travel.Cancelled", nameof(TravelRequest), request.Id.ToString(), details: new { reason });
        await NotifyAsync(request, "Travel request cancelled",
            $"Your trip to {request.Destination} was cancelled.");
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task NotifyAsync(TravelRequest request, string title, string body)
    {
        var userId = await _db.UserAccounts.AsNoTracking()
            .Where(u => u.EmployeeId == request.EmployeeId && u.Status == Domain.Identity.UserStatus.Active)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync();
        if (userId is null) return;

        await _notifications.PublishAsync(new NotificationMessage(
            userId, "Travel", title, body, DeepLink: "/travel-requests", Category: "Expense",
            EmployeeId: request.EmployeeId));
    }
}
