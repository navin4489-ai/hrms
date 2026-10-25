using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wintech.Hrms.Api.Security;
using Wintech.Hrms.Api.Security.Auth;

namespace Wintech.Hrms.Api.Modules.Attendance;

/// <summary>Shift catalogue, personal roster, swap requests and holidays (docs/API_CONTRACT.md §7).</summary>
[ApiController]
[Authorize]
[Produces("application/json")]
public sealed class ShiftsController : ControllerBase
{
    private readonly IShiftService _shifts;

    public ShiftsController(IShiftService shifts) => _shifts = shifts;

    /// <summary>Configured shifts (FR-ADM-040, FR-EMP-050).</summary>
    [HttpGet("api/v1/shifts")]
    [ProducesResponseType(typeof(IReadOnlyList<ShiftDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ShiftDto>>> List(CancellationToken cancellationToken)
        => Ok(await _shifts.GetShiftsAsync(cancellationToken));

    [HttpGet("api/v1/admin/shifts")]
    [Permission(Permissions.AdminConfig)]
    public async Task<ActionResult<IReadOnlyList<AdminShiftDto>>> AdminList(CancellationToken cancellationToken)
        => Ok(await _shifts.GetAdminShiftsAsync(cancellationToken));

    [HttpPost("api/v1/admin/shifts")]
    [Permission(Permissions.AdminConfig)]
    [ProducesResponseType(typeof(AdminShiftDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<AdminShiftDto>> CreateShift([FromBody] UpsertShiftRequest request,
        CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created,
            await _shifts.UpsertShiftAsync(null, request, cancellationToken));

    [HttpPut("api/v1/admin/shifts/{id:guid}")]
    [Permission(Permissions.AdminConfig)]
    public async Task<ActionResult<AdminShiftDto>> UpdateShift(Guid id,
        [FromBody] UpsertShiftRequest request, CancellationToken cancellationToken)
        => Ok(await _shifts.UpsertShiftAsync(id, request, cancellationToken));

    /// <summary>The caller's published roster for a date range (FR-EMP-050).</summary>
    [HttpGet("api/v1/shifts/roster")]
    [ProducesResponseType(typeof(IReadOnlyList<RosterEntryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RosterEntryDto>>> Roster(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
        => Ok(await _shifts.GetRosterAsync(from, to, cancellationToken));

    /// <summary>Request a shift swap with a colleague, routed to manager approval (FR-EMP-051).</summary>
    [HttpPost("api/v1/shifts/swap-requests")]
    [ProducesResponseType(typeof(ShiftSwapCreatedDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ShiftSwapCreatedDto>> CreateSwapRequest([FromBody] ShiftSwapRequestDto request,
        CancellationToken cancellationToken)
    {
        var created = await _shifts.CreateSwapRequestAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }
}

/// <summary>Published holidays for a calendar year (FR-ADM-055, FR-EMP-050).</summary>
[ApiController]
[Route("api/v1/holidays")]
[Authorize]
[Produces("application/json")]
public sealed class HolidaysController : ControllerBase
{
    private readonly IHolidayService _holidays;

    public HolidaysController(IHolidayService holidays) => _holidays = holidays;

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<HolidayDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<HolidayDto>>> List([FromQuery] int? year,
        [FromQuery] Guid? locationId,
        CancellationToken cancellationToken)
        => Ok(await _holidays.GetHolidaysAsync(year, locationId, cancellationToken));

    [HttpPost]
    [Permission(Permissions.AdminConfig)]
    [ProducesResponseType(typeof(HolidayDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<HolidayDto>> Create([FromBody] UpsertHolidayRequest request,
        CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created,
            await _holidays.CreateHolidayAsync(request, cancellationToken));

    [HttpPut("{id:guid}")]
    [Permission(Permissions.AdminConfig)]
    [ProducesResponseType(typeof(HolidayDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<HolidayDto>> Update(Guid id, [FromBody] UpsertHolidayRequest request,
        CancellationToken cancellationToken)
        => Ok(await _holidays.UpdateHolidayAsync(id, request, cancellationToken));
}
