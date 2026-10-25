using Wintech.Hrms.Api.Common.Paging;
using Wintech.Hrms.Api.Common.Registration;

namespace Wintech.Hrms.Api.Modules.Attendance;

/// <summary>Attendance calendars, punching and summaries (docs/API_CONTRACT.md §7, FR-EMP-030..038).</summary>
public interface IAttendanceService : IAppService
{
    /// <summary>Month calendar for one employee (mine, or a team member with attendance.view.team).</summary>
    Task<AttendanceMonthResponse> GetMonthAsync(string? month, Guid? employeeId, PageRequest page,
        CancellationToken cancellationToken = default);

    /// <summary>Today's record in the month-item shape; NotMarked defaults when absent.</summary>
    Task<AttendanceDayDto> GetTodayAsync(Guid? employeeId, CancellationToken cancellationToken = default);

    /// <summary>Dashboard slice payload (contract §5 attendance key).</summary>
    Task<DashboardAttendanceDto> GetTodayDashboardAsync(Guid? employeeId, CancellationToken cancellationToken = default);

    /// <summary>Aggregated summary over an arbitrary range (FR-EMP-030).</summary>
    Task<AttendanceRangeSummaryDto> GetRangeSummaryAsync(DateOnly from, DateOnly to,
        CancellationToken cancellationToken = default);

    /// <summary>Check-in/check-out punch with duplicate/window rules (FR-EMP-031/032/037).</summary>
    Task<PunchResponse> PunchAsync(PunchForm form, CancellationToken cancellationToken = default);

    /// <summary>Capture settings the employee app reads before punching (location/photo on-off).</summary>
    Task<AttendanceCaptureSettingsDto> GetCaptureSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>Admin attendance + leave analytics over a date range (FR-ADM-001/041) — admin-only.</summary>
    Task<AdminAttendanceDashboardDto> GetAdminDashboardAsync(DateOnly from, DateOnly to,
        Guid? departmentId, Guid? locationId, CancellationToken cancellationToken = default);

    /// <summary>Admin-only list of captured punch events (location, selfie, timestamp).</summary>
    Task<PagedResult<AdminPunchEventDto>> GetPunchEventsAsync(DateOnly from, DateOnly to,
        Guid? employeeId, Guid? departmentId, Guid? locationId, PageRequest page,
        CancellationToken cancellationToken = default);

    /// <summary>Admin-only download of a punch selfie by event id.</summary>
    Task<Common.Files.StoredFileStream> OpenPunchPhotoAsync(Guid eventId, CancellationToken cancellationToken = default);
}
