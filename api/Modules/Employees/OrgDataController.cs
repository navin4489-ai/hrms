using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
}
