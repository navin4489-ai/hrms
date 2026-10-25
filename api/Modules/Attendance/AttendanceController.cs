using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wintech.Hrms.Api.Common.Files;
using Wintech.Hrms.Api.Common.Paging;
using Wintech.Hrms.Api.Security;
using Wintech.Hrms.Api.Security.Auth;

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

    /// <summary>Capture settings the app reads before punching (what to collect: location/photo).</summary>
    [HttpGet("capture-settings")]
    [ProducesResponseType(typeof(AttendanceCaptureSettingsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AttendanceCaptureSettingsDto>> CaptureSettings(CancellationToken cancellationToken)
        => Ok(await _attendance.GetCaptureSettingsAsync(cancellationToken));

    /// <summary>
    /// Check-in / check-out punch (FR-EMP-031/032/037; 409 on duplicate punch). Accepts an optional
    /// selfie and geolocation as multipart form data; captured data is admin-only.
    /// </summary>
    [HttpPost("punch")]
    [Consumes("multipart/form-data", "application/json")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    [ProducesResponseType(typeof(PunchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PunchResponse>> Punch([FromForm] PunchForm form,
        CancellationToken cancellationToken)
        => Ok(await _attendance.PunchAsync(form, cancellationToken));

    /// <summary>Aggregated counts and worked hours over a date range (FR-EMP-030).</summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(AttendanceRangeSummaryDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AttendanceRangeSummaryDto>> GetSummary(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken cancellationToken)
        => Ok(await _attendance.GetRangeSummaryAsync(from, to, cancellationToken));
}

/// <summary>Admin attendance dashboard and punch-capture viewer (FR-ADM-001/041) — admin-only.</summary>
[ApiController]
[Route("api/v1/admin/attendance")]
[Authorize]
[Produces("application/json")]
public sealed class AttendanceAdminController : ControllerBase
{
    private readonly IAttendanceService _attendance;

    public AttendanceAdminController(IAttendanceService attendance) => _attendance = attendance;

    /// <summary>Attendance + leave analytics over a range for charts, filters and totals.</summary>
    [HttpGet("dashboard")]
    [Permission(Permissions.AttendanceManage)]
    [ProducesResponseType(typeof(AdminAttendanceDashboardDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminAttendanceDashboardDto>> Dashboard(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromQuery] Guid? departmentId, [FromQuery] Guid? locationId, CancellationToken cancellationToken)
        => Ok(await _attendance.GetAdminDashboardAsync(from, to, departmentId, locationId, cancellationToken));

    /// <summary>Captured punch events with location, selfie reference and timestamp.</summary>
    [HttpGet("punch-events")]
    [Permission(Permissions.AttendanceManage)]
    [ProducesResponseType(typeof(PagedResult<AdminPunchEventDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AdminPunchEventDto>>> PunchEvents(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? employeeId,
        [FromQuery] Guid? departmentId, [FromQuery] Guid? locationId,
        [FromQuery] PageRequest page, CancellationToken cancellationToken)
        => Ok(await _attendance.GetPunchEventsAsync(
            from, to, employeeId, departmentId, locationId, page, cancellationToken));

    /// <summary>Download a captured punch selfie (audited, admin-only; never a public URL).</summary>
    [HttpGet("punch-events/{id:guid}/photo")]
    [Permission(Permissions.AttendanceManage)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PunchPhoto(Guid id, CancellationToken cancellationToken)
    {
        StoredFileStream file = await _attendance.OpenPunchPhotoAsync(id, cancellationToken);
        return File(file.Content, file.ContentType, file.OriginalFileName);
    }
}
