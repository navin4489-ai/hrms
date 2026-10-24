using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wintech.Hrms.Api.Domain.Lifecycle;

namespace Wintech.Hrms.Api.Modules.Lifecycle;

/// <summary>Resignation &amp; exit endpoints (API contract §21, FR-EMP-130..133).</summary>
[ApiController]
[Route("api/v1/resignations")]
[Produces("application/json")]
[Authorize]
public sealed class ResignationsController : ControllerBase
{
    private readonly ILifecycleService _lifecycle;

    public ResignationsController(ILifecycleService lifecycle) => _lifecycle = lifecycle;

    /// <summary>Submit a resignation; routed through the generic approval engine (FR-EMP-130).</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ResignationCreateResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Common.ErrorHandling.ApiError), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(Common.ErrorHandling.ApiError), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ResignationCreateResponse>> Create([FromBody] CreateResignationRequest request,
        CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created,
            await _lifecycle.CreateResignationAsync(request, cancellationToken));

    /// <summary>Own resignations only; optional <c>status</c> filter (FR-EMP-131).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ResignationListItem>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ResignationListItem>>> List(
        [FromQuery] ResignationStatus? status, CancellationToken cancellationToken)
        => Ok(await _lifecycle.ListResignationsAsync(status, cancellationToken));

    /// <summary>Resignation detail with decision history and exit checklist (others: employee.manage).</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ResignationDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Common.ErrorHandling.ApiError), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ResignationDetailDto>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await _lifecycle.GetResignationAsync(id, cancellationToken));

    /// <summary>Withdraw a still-pending resignation (FR-EMP-130).</summary>
    [HttpPost("{id:guid}/withdraw")]
    [ProducesResponseType(typeof(WithdrawResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Common.ErrorHandling.ApiError), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<WithdrawResponse>> Withdraw(Guid id, [FromBody] WithdrawRequest request,
        CancellationToken cancellationToken)
        => Ok(await _lifecycle.WithdrawResignationAsync(id, request.Reason, cancellationToken));

    /// <summary>Exit checklist items (FR-EMP-131/132).</summary>
    [HttpGet("{id:guid}/checklist")]
    [ProducesResponseType(typeof(IReadOnlyList<ExitChecklistItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ExitChecklistItemDto>>> GetChecklist(Guid id,
        CancellationToken cancellationToken)
        => Ok(await _lifecycle.GetExitChecklistAsync(id, cancellationToken));

    /// <summary>Complete one exit checklist item assigned to the caller (FR-EMP-132).</summary>
    [HttpPost("{id:guid}/checklist/{itemId:guid}/complete")]
    [ProducesResponseType(typeof(ExitChecklistItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Common.ErrorHandling.ApiError), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ExitChecklistItemDto>> CompleteItem(Guid id, Guid itemId,
        [FromBody] CompleteExitItemRequest request, CancellationToken cancellationToken)
        => Ok(await _lifecycle.CompleteExitItemAsync(id, itemId, request, cancellationToken));

    /// <summary>Submit the exit survey — only after approval, once (FR-EMP-132).</summary>
    [HttpPost("{id:guid}/exit-survey")]
    [ProducesResponseType(typeof(ExitSurveyDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Common.ErrorHandling.ApiError), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ExitSurveyDto>> SubmitExitSurvey(Guid id, [FromBody] ExitSurveyRequest request,
        CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created,
            await _lifecycle.SubmitExitSurveyAsync(id, request, cancellationToken));
}
