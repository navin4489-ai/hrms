using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wintech.Hrms.Api.Common.Paging;
using Wintech.Hrms.Api.Domain.Lifecycle;
using Wintech.Hrms.Api.Security;
using Wintech.Hrms.Api.Security.Auth;

namespace Wintech.Hrms.Api.Modules.Lifecycle;

/// <summary>Onboarding &amp; offboarding checklist endpoints (API contract §22, FR-ADM-025/031..035).</summary>
[ApiController]
[Route("api/v1/lifecycle")]
[Produces("application/json")]
[Authorize]
public sealed class LifecycleController : ControllerBase
{
    private readonly ILifecycleService _lifecycle;

    public LifecycleController(ILifecycleService lifecycle) => _lifecycle = lifecycle;

    /// <summary>
    /// Paged checklist list. Reading is self-healing: joiners of the last 90 days without an
    /// onboarding checklist get one created from the default template (FR-ADM-025).
    /// </summary>
    [HttpGet("checklists")]
    [Permission(Permissions.EmployeeView)]
    [ProducesResponseType(typeof(PagedResult<ChecklistListItem>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ChecklistListItem>>> ListChecklists(
        [FromQuery] PageRequest page, [FromQuery] LifecycleChecklistType? type, [FromQuery] string? status,
        [FromQuery] string? q, CancellationToken cancellationToken)
        => Ok(await _lifecycle.ListChecklistsAsync(page, type, status, q, cancellationToken));

    /// <summary>Materialise a checklist for an employee from a template (FR-ADM-031).</summary>
    [HttpPost("checklists")]
    [Permission(Permissions.EmployeeCreate)]
    [ProducesResponseType(typeof(ChecklistDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Common.ErrorHandling.ApiError), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ChecklistDetailDto>> CreateChecklist([FromBody] CreateChecklistRequest request,
        CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created,
            await _lifecycle.CreateChecklistAsync(request, cancellationToken));

    /// <summary>Checklist detail with tasks and audit trail (FR-ADM-035).</summary>
    [HttpGet("checklists/{id:guid}")]
    [Permission(Permissions.EmployeeView)]
    [ProducesResponseType(typeof(ChecklistDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Common.ErrorHandling.ApiError), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ChecklistDetailDto>> GetChecklist(Guid id, CancellationToken cancellationToken)
        => Ok(await _lifecycle.GetChecklistAsync(id, cancellationToken));

    /// <summary>Transition one checklist task (Start|Complete|Reopen|Waive); audited, 409 when illegal.</summary>
    [HttpPost("checklists/{id:guid}/tasks/{taskId:guid}")]
    [Permission(Permissions.EmployeeManage)]
    [ProducesResponseType(typeof(ChecklistTaskDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Common.ErrorHandling.ApiError), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ChecklistTaskDto>> TransitionTask(Guid id, Guid taskId,
        [FromBody] TransitionTaskRequest request, CancellationToken cancellationToken)
    {
        if (request.Action is null)
            throw new Common.ErrorHandling.RequestValidationException(errors: new()
            {
                ["action"] = ["An action is required."]
            });

        return Ok(await _lifecycle.TransitionTaskAsync(id, taskId, request.Action.Value, request.Note,
            cancellationToken));
    }

    /// <summary>Default onboarding/offboarding templates with their tasks.</summary>
    [HttpGet("templates")]
    [Permission(Permissions.EmployeeCreate)]
    [ProducesResponseType(typeof(IReadOnlyList<LifecycleTemplateDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<LifecycleTemplateDto>>> ListTemplates(
        CancellationToken cancellationToken)
        => Ok(await _lifecycle.ListTemplatesAsync(cancellationToken));
}
