using Wintech.Hrms.Api.Domain.Approvals;

namespace Wintech.Hrms.Api.Modules.Lifecycle;

/// <summary>
/// Approval engine callback for <c>RequestType == "Resignation"</c> (contract §21, FR-EMP-130/131):
/// Approved -> create the six-task exit checklist and set the relieving date;
/// Rejected/Cancelled -> revert the resignation status. Delegates all state changes to
/// <see cref="ILifecycleService"/> so they are audited and notified in one unit of work.
/// </summary>
public sealed class ResignationApprovalHandler : IApprovalHandler
{
    private readonly ILifecycleService _lifecycle;

    public ResignationApprovalHandler(ILifecycleService lifecycle) => _lifecycle = lifecycle;

    public string RequestType => "Resignation";

    public Task OnCompletedAsync(ApprovalTask task, ApprovalActionType finalAction, string? comment,
        CancellationToken cancellationToken = default)
        => _lifecycle.ApplyApprovalOutcomeAsync(task, finalAction, comment, cancellationToken);

    public Task OnCancelledAsync(ApprovalTask task, string? reason, CancellationToken cancellationToken = default)
        => _lifecycle.CancelResignationAsync(task, reason, cancellationToken);
}
