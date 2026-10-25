using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wintech.Hrms.Api.Security;
using Wintech.Hrms.Api.Security.Auth;

namespace Wintech.Hrms.Api.Modules.Employees;

/// <summary>Reference data for filters and pickers (contract §6).</summary>
[ApiController]
[Route("api/v1")]
[Authorize]
[Produces("application/json")]
public sealed class OrgDataController : ControllerBase
{
    private readonly IOrgDataService _orgData;

    public OrgDataController(IOrgDataService orgData) => _orgData = orgData;

    /// <summary>Legal entities, business units, departments and teams.</summary>
    [HttpGet("org-units")]
    [ProducesResponseType(typeof(IReadOnlyList<OrgUnitDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<OrgUnitDto>>> OrgUnits(CancellationToken cancellationToken)
        => Ok(await _orgData.GetOrgUnitsAsync(cancellationToken));

    /// <summary>Work locations.</summary>
    [HttpGet("locations")]
    [ProducesResponseType(typeof(IReadOnlyList<LocationDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<LocationDto>>> Locations(CancellationToken cancellationToken)
        => Ok(await _orgData.GetLocationsAsync(cancellationToken));

    /// <summary>Designations with grade.</summary>
    [HttpGet("designations")]
    [ProducesResponseType(typeof(IReadOnlyList<DesignationDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DesignationDto>>> Designations(CancellationToken cancellationToken)
        => Ok(await _orgData.GetDesignationsAsync(cancellationToken));

    /// <summary>Create a legal entity, business unit, department or team (FR-ADM-010).</summary>
    [HttpPost("org-units")]
    [Permission(Permissions.AdminConfig)]
    [ProducesResponseType(typeof(OrgUnitDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<OrgUnitDto>> CreateOrgUnit(
        [FromBody] UpsertOrgUnitRequest request, CancellationToken cancellationToken)
    {
        var created = await _orgData.CreateOrgUnitAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    /// <summary>Update organization reference data without deleting historical references.</summary>
    [HttpPut("org-units/{id:guid}")]
    [Permission(Permissions.AdminConfig)]
    [ProducesResponseType(typeof(OrgUnitDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<OrgUnitDto>> UpdateOrgUnit(Guid id,
        [FromBody] UpsertOrgUnitRequest request, CancellationToken cancellationToken)
        => Ok(await _orgData.UpdateOrgUnitAsync(id, request, cancellationToken));

    [HttpPost("locations")]
    [Permission(Permissions.AdminConfig)]
    [ProducesResponseType(typeof(LocationDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<LocationDto>> CreateLocation(
        [FromBody] UpsertLocationRequest request, CancellationToken cancellationToken)
    {
        var created = await _orgData.CreateLocationAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpPut("locations/{id:guid}")]
    [Permission(Permissions.AdminConfig)]
    [ProducesResponseType(typeof(LocationDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<LocationDto>> UpdateLocation(Guid id,
        [FromBody] UpsertLocationRequest request, CancellationToken cancellationToken)
        => Ok(await _orgData.UpdateLocationAsync(id, request, cancellationToken));

    [HttpPost("designations")]
    [Permission(Permissions.AdminConfig)]
    [ProducesResponseType(typeof(DesignationDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<DesignationDto>> CreateDesignation(
        [FromBody] UpsertDesignationRequest request, CancellationToken cancellationToken)
    {
        var created = await _orgData.CreateDesignationAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpPut("designations/{id:guid}")]
    [Permission(Permissions.AdminConfig)]
    [ProducesResponseType(typeof(DesignationDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<DesignationDto>> UpdateDesignation(Guid id,
        [FromBody] UpsertDesignationRequest request, CancellationToken cancellationToken)
        => Ok(await _orgData.UpdateDesignationAsync(id, request, cancellationToken));
}
