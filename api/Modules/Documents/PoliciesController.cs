using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Wintech.Hrms.Api.Modules.Documents;

/// <summary>Company policies and versioned acknowledgements (contract §11, FR-EMP-074).</summary>
[ApiController]
[Route("api/v1/policies")]
[Produces("application/json")]
public sealed class PoliciesController : ControllerBase
{
    private readonly IPolicyService _policies;

    public PoliciesController(IPolicyService policies) => _policies = policies;

    /// <summary>List active policies with the caller's acknowledgement state for the current version.</summary>
    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<CompanyPolicyDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CompanyPolicyDto>>> List(
        [FromQuery] string? category,
        [FromQuery] string? acknowledgementStatus,
        CancellationToken cancellationToken)
        => Ok(await _policies.GetPoliciesAsync(category, acknowledgementStatus, cancellationToken));

    /// <summary>Policy detail including the short policy body (or documentId for the full text).</summary>
    [HttpGet("{id:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(PolicyDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PolicyDetailDto>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await _policies.GetPolicyAsync(id, cancellationToken));

    /// <summary>
    /// Acknowledge the policy's current version (FR-EMP-074). Acknowledgement is versioned —
    /// acknowledging the same version again returns 409.
    /// </summary>
    [HttpPost("{id:guid}/acknowledge")]
    [Authorize]
    [ProducesResponseType(typeof(AcknowledgePolicyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AcknowledgePolicyResponse>> Acknowledge(
        Guid id, [FromBody] AcknowledgePolicyRequest request, CancellationToken cancellationToken)
        => Ok(await _policies.AcknowledgeAsync(id, request.Comment, cancellationToken));
}
