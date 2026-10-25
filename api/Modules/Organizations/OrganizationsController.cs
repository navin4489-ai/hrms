using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wintech.Hrms.Api.Security;
using Wintech.Hrms.Api.Security.Auth;

namespace Wintech.Hrms.Api.Modules.Organizations;

/// <summary>
/// SuperAdmin organization administration (FR-ADM-001/002). Creating/configuring organizations and
/// per-org module flags requires <see cref="Permissions.AdminOrganizations"/> (held by SuperAdmin).
/// The caller's own enabled modules are exposed to any authenticated user via <c>/me/modules</c>.
/// </summary>
[ApiController]
[Route("api/v1")]
[Authorize]
[Produces("application/json")]
public sealed class OrganizationsController : ControllerBase
{
    private readonly IOrganizationService _organizations;

    public OrganizationsController(IOrganizationService organizations) => _organizations = organizations;

    [HttpGet("admin/organizations")]
    [Permission(Permissions.AdminOrganizations)]
    [ProducesResponseType(typeof(IReadOnlyList<OrganizationDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<OrganizationDto>>> List(CancellationToken cancellationToken)
        => Ok(await _organizations.ListAsync(cancellationToken));

    [HttpGet("admin/organizations/{id:guid}")]
    [Permission(Permissions.AdminOrganizations)]
    [ProducesResponseType(typeof(OrganizationDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrganizationDetailDto>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await _organizations.GetAsync(id, cancellationToken));

    [HttpPost("admin/organizations")]
    [Permission(Permissions.AdminOrganizations)]
    [ProducesResponseType(typeof(OrganizationDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrganizationDetailDto>> Create([FromBody] CreateOrganizationRequest request,
        CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await _organizations.CreateAsync(request, cancellationToken));

    [HttpPut("admin/organizations/{id:guid}")]
    [Permission(Permissions.AdminOrganizations)]
    [ProducesResponseType(typeof(OrganizationDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrganizationDetailDto>> Update(Guid id,
        [FromBody] UpdateOrganizationRequest request, CancellationToken cancellationToken)
        => Ok(await _organizations.UpdateAsync(id, request, cancellationToken));

    [HttpPut("admin/organizations/{id:guid}/modules")]
    [Permission(Permissions.AdminOrganizations)]
    [ProducesResponseType(typeof(OrganizationDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrganizationDetailDto>> UpdateModules(Guid id,
        [FromBody] UpdateOrgModulesRequest request, CancellationToken cancellationToken)
        => Ok(await _organizations.UpdateModulesAsync(id, request, cancellationToken));

    /// <summary>Modules enabled for the caller's own organization (used by apps to hide disabled features).</summary>
    [HttpGet("me/modules")]
    [ProducesResponseType(typeof(IReadOnlyList<OrgModuleFlagDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<OrgModuleFlagDto>>> MyModules(CancellationToken cancellationToken)
        => Ok(await _organizations.GetMyModulesAsync(cancellationToken));
}
