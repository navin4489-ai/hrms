using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wintech.Hrms.Api.Common.CurrentUser;
using Wintech.Hrms.Api.Common.ErrorHandling;
using Wintech.Hrms.Api.Common.Paging;
using Wintech.Hrms.Api.Domain.Approvals;

namespace Wintech.Hrms.Api.Modules.Approvals;

/// <summary>
/// Approvals centre (contract section 9, FR-MGR-001..007). Every module routes its approval
/// decisions through here so BR-004 (no self-approval) and BR-007 (reject reason) are enforced
/// centrally by <see cref="IApprovalService"/>.
/// </summary>
[ApiController]
[Route("api/v1/approvals")]
[Produces("application/json")]
public sealed class ApprovalsController : ControllerBase
{
    private readonly IApprovalService _approvals;
    private readonly IApprovalQueryService _queries;
    private readonly ICurrentUser _currentUser;

    public ApprovalsController(IApprovalService approvals, IApprovalQueryService queries, ICurrentUser currentUser)
    {
        _approvals = approvals;
        _queries = queries;
        _currentUser = currentUser;
    }

    /// <summary>Tasks awaiting the caller, including delegated assignments (FR-MGR-001).</summary>
    [HttpGet("pending")]
    [Authorize]
    [ProducesResponseType(typeof(PagedResult<PendingApprovalItem>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<PendingApprovalItem>>> Pending([FromQuery] PageRequest page,
        CancellationToken cancellationToken)
        => Ok(await _queries.GetPendingAsync(page, cancellationToken));

    /// <summary>The caller's own requests and their workflow state.</summary>
    [HttpGet("mine")]
    [Authorize]
    [ProducesResponseType(typeof(PagedResult<MyApprovalItem>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<MyApprovalItem>>> Mine([FromQuery] PageRequest page,
        CancellationToken cancellationToken)
        => Ok(await _queries.GetMineAsync(page, cancellationToken));

    /// <summary>Pending/overdue counts for the caller plus decisions completed this month.</summary>
    [HttpGet("stats")]
    [Authorize]
    [ProducesResponseType(typeof(ApprovalStatsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApprovalStatsDto>> Stats(CancellationToken cancellationToken)
        => Ok(await _queries.GetStatsAsync(cancellationToken));

    /// <summary>Full task detail with the decision history (FR-MGR-002, FR-ADM-065).</summary>
    [HttpGet("{taskId:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(ApprovalDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApprovalDetailDto>> Detail(Guid taskId, CancellationToken cancellationToken)
    {
        var detail = await _queries.GetDetailAsync(taskId, cancellationToken);
        return detail is null ? NotFound() : Ok(detail);
    }

    /// <summary>
    /// Approve / Reject / Return the current step (FR-MGR-002). 403 when the task is not yours or
    /// you are its requester (BR-004); 409 when already decided or stale; 422 when Reject/Return
    /// carries no comment (BR-007).
    /// </summary>
    [HttpPost("{taskId:guid}/decision")]
    [Authorize]
    [ProducesResponseType(typeof(ApprovalDecisionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ApprovalDecisionResult>> Decide(Guid taskId,
        [FromBody] ApprovalDecisionRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ApprovalActionType>(request.Action, ignoreCase: true, out var action) ||
            action is not (ApprovalActionType.Approve or ApprovalActionType.Reject or ApprovalActionType.Return))
        {
            throw new RequestValidationException("The decision action is invalid.",
                new() { ["action"] = ["Must be Approve, Reject or Return."] });
        }

        var task = await _approvals.DecideAsync(taskId, action, request.Comment,
            _currentUser.RequireUserId(), cancellationToken);

        var completed = !string.Equals(task.Status, nameof(ApprovalStatus.Pending), StringComparison.Ordinal);
        var message = completed
            ? task.Status switch
            {
                nameof(ApprovalStatus.Approved) => "The request was approved.",
                nameof(ApprovalStatus.Rejected) => "The request was rejected.",
                nameof(ApprovalStatus.Returned) => "The request was returned to the requester.",
                _ => $"The request is now {task.Status}."
            }
            : $"Approved. Routed to the next step: {task.CurrentStepName}.";

        return Ok(new ApprovalDecisionResult(task.Id, task.Status, completed, message));
    }

    /// <summary>Delegate your assignment to another active user for a server-defined period (FR-MGR-003).</summary>
    [HttpPost("{taskId:guid}/delegate")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Delegate(Guid taskId, [FromBody] ApprovalDelegateRequest request,
        CancellationToken cancellationToken)
    {
        await _approvals.DelegateAsync(taskId, request.ToUserId, request.Reason,
            _currentUser.RequireUserId(), cancellationToken);
        return NoContent();
    }
}
