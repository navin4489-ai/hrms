using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.CurrentUser;
using Wintech.Hrms.Api.Common.Dashboard;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Attendance;

namespace Wintech.Hrms.Api.Modules.Attendance;

/// <summary>
/// Contributes the <c>attendance</c> and <c>holidays</c> keys of GET /me/dashboard
/// (docs/API_CONTRACT.md §5, FR-EMP-010..014).
/// </summary>
public sealed class AttendanceDashboardSliceProvider : IDashboardSliceProvider
{
    private const int UpcomingHolidaysLimit = 10;

    private readonly IAttendanceService _attendance;
    private readonly ICurrentUser _currentUser;
    private readonly AppDbContext _db;

    public AttendanceDashboardSliceProvider(IAttendanceService attendance,
        ICurrentUser currentUser, AppDbContext db)
    {
        _attendance = attendance;
        _currentUser = currentUser;
        _db = db;
    }

    public int Order => 100;

    public async Task<IReadOnlyDictionary<string, object?>> GetSliceAsync(DashboardContext context,
        CancellationToken cancellationToken = default)
    {
        var attendance = await _attendance.GetTodayDashboardAsync(
            _currentUser.EmployeeId ?? context.EmployeeId, cancellationToken);

        var today = context.Today;
        var locationId = await CurrentLocationIdAsync(context, cancellationToken);
        var upcoming = await _db.Set<CompanyHoliday>().AsNoTracking()
            .Where(h => h.Date >= today && (h.LocationId == null || h.LocationId == locationId))
            .OrderBy(h => h.Date)
            .Take(UpcomingHolidaysLimit)
            .Select(h => new DashboardHolidayDto(h.Date, h.Name))
            .ToListAsync(cancellationToken);

        return new Dictionary<string, object?>
        {
            ["attendance"] = attendance,
            ["holidays"] = upcoming
        };
    }

    private async Task<Guid?> CurrentLocationIdAsync(DashboardContext context, CancellationToken cancellationToken)
    {
        var employeeId = _currentUser.EmployeeId ?? context.EmployeeId;
        if (employeeId is null) return null;
        var employee = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);
        return employee?.LocationId;
    }
}
