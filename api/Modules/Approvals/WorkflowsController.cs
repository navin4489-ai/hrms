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

    public WorkflowsController(IApprovalQueryService queries) => _queries = queries;

    /// <summary>List every workflow definition with its steps (admin.config).</summary>
    [HttpGet]
    [Authorize]
    [Permission(Permissions.AdminConfig)]
    [ProducesResponseType(typeof(IReadOnlyList<WorkflowDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<WorkflowDto>>> List(CancellationToken cancellationToken)
        => Ok(await _queries.GetWorkflowsAsync(cancellationToken));
}
