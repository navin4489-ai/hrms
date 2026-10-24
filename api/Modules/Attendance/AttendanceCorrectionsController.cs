using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wintech.Hrms.Api.Common.Paging;

namespace Wintech.Hrms.Api.Modules.Attendance;

/// <summary>Attendance corrections routed through the approval engine (docs/API_CONTRACT.md §7).</summary>
[ApiController]
[Route("api/v1/attendance-corrections")]
[Authorize]
[Produces("application/json")]
public sealed class AttendanceCorrectionsController : ControllerBase
{
    private readonly IAttendanceCorrectionService _corrections;

    public AttendanceCorrectionsController(IAttendanceCorrectionService corrections) => _corrections = corrections;

    /// <summary>Submit a correction request (FR-EMP-035..037; 409 duplicate open, 422 outside window).</summary>
    [HttpPost]
    [ProducesResponseType(typeof(AttendanceCorrectionCreatedDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AttendanceCorrectionCreatedDto>> Create([FromBody] AttendanceCorrectionCreateDto request,
        CancellationToken cancellationToken)
    {
        var created = await _corrections.CreateAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    /// <summary>List corrections (own records; team with `attendance.view.team` via `employeeId`).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<AttendanceCorrectionListItem>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AttendanceCorrectionListItem>>> List(
        [FromQuery] string? status, [FromQuery] string? month, [FromQuery] Guid? employeeId,
        [FromQuery] PageRequest page, CancellationToken cancellationToken)
        => Ok(await _corrections.ListAsync(status, month, employeeId, page, cancellationToken));

    /// <summary>Correction detail including the workflow `decisionHistory` (FR-EMP-036).</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AttendanceCorrectionDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AttendanceCorrectionDetail>> GetDetail([FromRoute] Guid id,
        CancellationToken cancellationToken)
        => Ok(await _corrections.GetDetailAsync(id, cancellationToken));
}
