using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wintech.Hrms.Api.Common.Paging;

namespace Wintech.Hrms.Api.Modules.Attendance;

/// <summary>Attendance calendars, punching and summaries (docs/API_CONTRACT.md §7, FR-EMP-030..038).</summary>
[ApiController]
[Route("api/v1/attendance")]
[Authorize]
[Produces("application/json")]
public sealed class AttendanceController : ControllerBase
{
    private readonly IAttendanceService _attendance;

    public AttendanceController(IAttendanceService attendance) => _attendance = attendance;

    /// <summary>
    /// Month calendar for the caller (or a team member with `attendance.view.team` when
    /// `employeeId` is supplied). The response carries the month summary alongside the paged rows.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(AttendanceMonthResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AttendanceMonthResponse>> GetMonth(
        [FromQuery] string? month, [FromQuery] Guid? employeeId, [FromQuery] PageRequest page,
        CancellationToken cancellationToken)
        => Ok(await _attendance.GetMonthAsync(month, employeeId, page, cancellationToken));

    /// <summary>Today's attendance in the month-item shape; `NotMarked` defaults when absent.</summary>
    [HttpGet("today")]
    [ProducesResponseType(typeof(AttendanceDayDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AttendanceDayDto>> GetToday([FromQuery] Guid? employeeId,
        CancellationToken cancellationToken)
        => Ok(await _attendance.GetTodayAsync(employeeId, cancellationToken));

    /// <summary>Check-in / check-out punch (FR-EMP-031/032/037; 409 on duplicate punch).</summary>
    [HttpPost("punch")]
    [ProducesResponseType(typeof(PunchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PunchResponse>> Punch([FromBody] PunchRequest request,
        CancellationToken cancellationToken)
        => Ok(await _attendance.PunchAsync(request, cancellationToken));

    /// <summary>Aggregated counts and worked hours over a date range (FR-EMP-030).</summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(AttendanceRangeSummaryDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AttendanceRangeSummaryDto>> GetSummary(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken cancellationToken)
        => Ok(await _attendance.GetRangeSummaryAsync(from, to, cancellationToken));
}
