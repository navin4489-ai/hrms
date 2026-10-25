using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wintech.Hrms.Api.Security;
using Wintech.Hrms.Api.Security.Auth;

namespace Wintech.Hrms.Api.Modules.Approvals;

/// <summary>Workflow definitions (FR-ADM-060..065). Read-only for administrators.</summary>
[ApiController]
[Route("api/v1/workflows")]
[Produces("application/json")]
public sealed class WorkflowsController : ControllerBase
{
    private readonly IApprovalQueryService _queries;
    private readonly IWorkflowAdministrationService _administration;

    public WorkflowsController(IApprovalQueryService queries, IWorkflowAdministrationService administration)
    {
        _queries = queries;
        _administration = administration;
    }

    /// <summary>List every workflow definition with its steps (admin.config).</summary>
    [HttpGet]
    [Authorize]
    [Permission(Permissions.AdminConfig)]
    [ProducesResponseType(typeof(IReadOnlyList<WorkflowDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<WorkflowDto>>> List(CancellationToken cancellationToken)
        => Ok(await _queries.GetWorkflowsAsync(cancellationToken));

    /// <summary>Publish a new immutable version; existing approval tasks continue using their snapshotted version.</summary>
    [HttpPost("{id:guid}/versions")]
    [Authorize]
    [Permission(Permissions.AdminConfig)]
    [ProducesResponseType(typeof(WorkflowDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<WorkflowDto>> CreateVersion(Guid id,
        [FromBody] UpsertWorkflowVersionRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created,
            await _administration.CreateVersionAsync(id, request, cancellationToken));
}
