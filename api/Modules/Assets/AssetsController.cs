using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wintech.Hrms.Api.Security;
using Wintech.Hrms.Api.Security.Auth;

namespace Wintech.Hrms.Api.Modules.Assets;

/// <summary>Assets (contract section 13, FR-EMP-090..093).</summary>
[ApiController]
[Route("api/v1/assets")]
[Produces("application/json")]
public sealed class AssetsController : ControllerBase
{
    private readonly IAssetService _assets;

    public AssetsController(IAssetService assets) => _assets = assets;

    /// <summary>Inventory overview with paging and the overdue-return counter (FR-ADM-093).</summary>
    [HttpGet("inventory")]
    [Authorize]
    [Permission(Permissions.AssetManage)]
    [ProducesResponseType(typeof(AssetInventoryResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AssetInventoryResult>> Inventory([FromQuery] AssetInventoryQuery query,
        [FromQuery] Common.Paging.PageRequest page, CancellationToken cancellationToken)
        => Ok(await _assets.ListInventoryAsync(query, page, cancellationToken));

    /// <summary>Add a new asset to the inventory (Available).</summary>
    [HttpPost]
    [Authorize]
    [Permission(Permissions.AssetManage)]
    [ProducesResponseType(typeof(AssetInventoryItem), StatusCodes.Status201Created)]
    public async Task<ActionResult<AssetInventoryItem>> Create([FromBody] CreateAssetForm form,
        CancellationToken cancellationToken)
    {
        var asset = await _assets.CreateAssetAsync(form, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, asset);
    }

    /// <summary>Allocate an available asset to an employee (409 when not Available — FR-ADM-093).</summary>
    [HttpPost("{id:guid}/assign")]
    [Authorize]
    [Permission(Permissions.AssetManage)]
    [ProducesResponseType(typeof(AssetAssignmentResult), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AssetAssignmentResult>> Assign(Guid id, [FromBody] AssignAssetForm form,
        CancellationToken cancellationToken)
    {
        var assignment = await _assets.AssignAsync(id, form, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, assignment);
    }

    /// <summary>Assets assigned to the caller (FR-EMP-090).</summary>
    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<AssetMineItem>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AssetMineItem>>> Mine(CancellationToken cancellationToken)
        => Ok(await _assets.ListMineAsync(cancellationToken));

    /// <summary>Acknowledge handover of an assigned asset (FR-EMP-091).</summary>
    [HttpPost("{id:guid}/acknowledge")]
    [Authorize]
    [ProducesResponseType(typeof(AssetMineItem), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AssetMineItem>> Acknowledge(Guid id, [FromBody] AcknowledgeAssetRequest request,
        CancellationToken cancellationToken)
        => Ok(await _assets.AcknowledgeAsync(id, request.Condition, request.Note, cancellationToken));

    /// <summary>
    /// Request the return of an assigned asset (FR-EMP-093: employees cannot mark an asset
    /// returned themselves — asset.manage verifies and completes the handover).
    /// </summary>
    [HttpPost("{id:guid}/return-request")]
    [Authorize]
    [ProducesResponseType(typeof(AssetReturnResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AssetReturnResult>> RequestReturn(Guid id, [FromBody] ReturnAssetRequest request,
        CancellationToken cancellationToken)
        => Ok(await _assets.RequestReturnAsync(id, request.Reason, request.Condition, cancellationToken));
}
