using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Auditing;
using Wintech.Hrms.Api.Common.Configuration;
using Wintech.Hrms.Api.Common.CurrentUser;
using Wintech.Hrms.Api.Common.ErrorHandling;
using Wintech.Hrms.Api.Common.Paging;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Attendance;
using Wintech.Hrms.Api.Domain.Employees;
using Wintech.Hrms.Api.Domain.Leave;
using Wintech.Hrms.Api.Security;

namespace Wintech.Hrms.Api.Modules.Attendance;

public sealed class AttendanceService : IAttendanceService
{
    /// <summary>Minimum gap between two punches before the second one counts as a check-out rather than a duplicate (FR-EMP-037).</summary>
    private const int DuplicatePunchGapMinutes = 5;

    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly SecurityOptions _securityOptions;

    public AttendanceService(AppDbContext db, ICurrentUser currentUser, IClock clock,
        IAuditService audit, SecurityOptions securityOptions)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
        _securityOptions = securityOptions;
    }

    // ---------------------------------------------------------------- month / today

    public async Task<AttendanceMonthResponse> GetMonthAsync(string? month, Guid? employeeId,
        PageRequest page, CancellationToken cancellationToken = default)
    {
        var monthStart = ParseMonth(month);
        var from = AttendanceTimeHelper.FirstDayOfMonth(monthStart);
        var to = AttendanceTimeHelper.LastDayOfMonth(monthStart);

        var employee = await ResolveTargetAsync(employeeId, cancellationToken);
        var calendar = await BuildCalendarAsync(employee, from, to, cancellationToken);

        var days = new List<AttendanceDayDto>(to.DayNumber - from.DayNumber + 1);
        for (var date = from; date <= to; date = date.AddDays(1))
            days.Add(calendar.ToDayDto(date));

        var summary = Summarise(days, calendar.PendingCorrectionCount);

        var paged = days.ToPaged(page);
        return new AttendanceMonthResponse
        {
            Items = paged.Items,
            Page = paged.Page,
            PageSize = paged.PageSize,
            Total = paged.Total,
            HasNextPage = paged.HasNextPage,
            Summary = summary
        };
    }

    public async Task<AttendanceDayDto> GetTodayAsync(Guid? employeeId, CancellationToken cancellationToken = default)
    {
        var today = AttendanceTimeHelper.DateLocal(_clock.UtcNow);
        var employee = await ResolveTargetAsync(employeeId, cancellationToken);
        var calendar = await BuildCalendarAsync(employee, today, today, cancellationToken);
        return calendar.ToDayDto(today);
    }

    public async Task<DashboardAttendanceDto> GetTodayDashboardAsync(Guid? employeeId,
        CancellationToken cancellationToken = default)
    {
        var today = AttendanceTimeHelper.DateLocal(_clock.UtcNow);
        var employee = employeeId is null ? null : await ResolveTargetAsync(employeeId, cancellationToken);
        if (employee is null)
        {
            return new DashboardAttendanceDto(today, AttendanceDayStatus.NotMarked, null,
                null, null, null, false, false, null);
        }

        var calendar = await BuildCalendarAsync(employee, today, today, cancellationToken);
        var (status, shift, record, workingDay, _) = calendar.Describe(today);

        var checkIn = record?.CheckIn;
        var checkOut = record?.CheckOut;
        var canPunchIn = workingDay && status is not (AttendanceDayStatus.Leave or AttendanceDayStatus.Holiday)
                         && checkIn is null;
        var canPunchOut = checkIn is not null && checkOut is null;

        return new DashboardAttendanceDto(
            today,
            status,
            shift is null ? null : new DashboardShiftDto(shift.Name, shift.StartTime, shift.EndTime, shift.GraceMinutes),
            checkIn,
            checkOut,
            record?.WorkedHours,
            canPunchIn,
            canPunchOut,
            checkIn is null ? null : record?.LateByMinutes);
    }

    public async Task<AttendanceRangeSummaryDto> GetRangeSummaryAsync(DateOnly from, DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var employee = await ResolveTargetAsync(null, cancellationToken);
        if (to < from)
            throw new RequestValidationException(errors: new() { ["to"] = ["End date must be on or after start date."] });
        if (to.DayNumber - from.DayNumber > 366)
            throw new RequestValidationException(errors: new() { ["to"] = ["Range must not exceed one year."] });

        var calendar = await BuildCalendarAsync(employee, from, to, cancellationToken);
        var days = new List<AttendanceDayDto>();
        for (var date = from; date <= to; date = date.AddDays(1))
            days.Add(calendar.ToDayDto(date));

        decimal workedHours = 0, overtimeHours = 0;
        foreach (var record in calendar.Records.Values)
        {
            workedHours += record.WorkedHours ?? 0;
            overtimeHours += record.OvertimeHours;
        }

        var summary = Summarise(days, calendar.PendingCorrectionCount);
        return new AttendanceRangeSummaryDto(summary.Present, summary.Absent, summary.HalfDay, summary.Leave,
            summary.Holidays, summary.WeekOffs, summary.Late, overtimeHours, workedHours);
    }

    // ---------------------------------------------------------------- punching

    public async Task<PunchResponse> PunchAsync(PunchRequest request, CancellationToken cancellationToken = default)
    {
        var employeeId = RequireEmployeeId();
        var employee = await _db.Employees.AsNoTracking()
                           .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken)
                       ?? throw new ForbiddenException("Your user account is not linked to an employee record.");

        var localNow = AttendanceTimeHelper.NowLocal(_clock.UtcNow);
        var today = DateOnly.FromDateTime(localNow);
        var timeNow = TimeOnly.FromDateTime(localNow);

        if (_securityOptions.GeofenceEnabled && request.Latitude is null)
            throw new BusinessRuleException("Your location is required to punch in because geofencing is enabled.",
                new() { ["location"] = ["Enable location access or punch in from a registered work site."] });

        var calendar = await BuildCalendarAsync(employee, today, today, cancellationToken);
        var (_, shift, record, workingDay, holidayName) = calendar.Describe(today);

        if (!workingDay)
        {
            throw new ConflictException(holidayName is not null
                ? $"Today is a holiday ({holidayName}); punching is not required."
                : "You are not rostered to work today, so a punch cannot be recorded.");
        }

        if (record is null)
        {
            record = new AttendanceDayRecord
            {
                Id = Guid.NewGuid(),
                EmployeeId = employee.Id,
                WorkDate = today,
                ShiftId = shift?.Id,
                Status = AttendanceDayStatus.NotMarked,
                Source = ParseSource(request.Source),
                UpdatedAt = _clock.UtcNow
            };
            _db.Set<AttendanceDayRecord>().Add(record);
        }

        record.Source = ParseSource(request.Source);
        record.UpdatedAt = _clock.UtcNow;

        string message;
        if (record.CheckIn is null)
        {
            record.CheckIn = timeNow;
            record.LateByMinutes = shift is null
                ? 0
                : Math.Max(0, AttendanceTimeHelper.Minutes(timeNow)
                    - (AttendanceTimeHelper.Minutes(shift.StartTime) + shift.GraceMinutes));
            record.Status = record.LateByMinutes > 0 ? AttendanceDayStatus.Late : AttendanceDayStatus.Present;
            message = record.LateByMinutes > 0
                ? $"Checked in late by {record.LateByMinutes} minute(s)."
                : "Checked in.";
        }
        else if (record.CheckOut is null)
        {
            var gapMinutes = AttendanceTimeHelper.Minutes(timeNow) - AttendanceTimeHelper.Minutes(record.CheckIn.Value);
            if (gapMinutes < 0) gapMinutes += 24 * 60;
            if (gapMinutes < DuplicatePunchGapMinutes)
                throw new ConflictException(
                    $"You already checked in at {record.CheckIn:HH:mm}; this punch looks like a duplicate and was rejected.");

            record.CheckOut = timeNow;
            record.WorkedHours = AttendanceTimeHelper.WorkedHours(record.CheckIn, record.CheckOut, shift?.BreakMinutes ?? 0);
            record.OvertimeHours = record.WorkedHours is { } worked && worked > 8 ? Math.Round(worked - 8, 2) : 0;
            record.Status = record.IsHalfDay
                ? AttendanceDayStatus.HalfDay
                : record.LateByMinutes > 0 ? AttendanceDayStatus.Late : AttendanceDayStatus.Present;
            message = "Checked out.";
        }
        else
        {
            throw new ConflictException(
                $"You have already checked out for today at {record.CheckOut:HH:mm}. Use an attendance correction if this is wrong.");
        }

        _audit.Add("Attendance.Punched", nameof(AttendanceDayRecord), record.Id.ToString(),
            details: new
            {
                workDate = record.WorkDate,
                checkIn = record.CheckIn,
                checkOut = record.CheckOut,
                status = record.Status.ToString(),
                source = record.Source.ToString(),
                request.Latitude,
                request.Longitude,
                request.AccuracyMeters
            });
        await _db.SaveChangesAsync(cancellationToken);

        return new PunchResponse(record.WorkDate, record.CheckIn, record.CheckOut, record.Status, message);
    }

    // ---------------------------------------------------------------- calendar assembly

    private sealed class CalendarData
    {
        public required Guid EmployeeId { get; init; }
        public required DateOnly From { get; init; }
        public required DateOnly To { get; init; }
        public required WorkShift? DefaultShift { get; init; }
        public required IReadOnlyDictionary<DateOnly, WorkShift> Assignments { get; init; }
        public required IReadOnlyDictionary<DateOnly, AttendanceDayRecord> Records { get; init; }
        public required IReadOnlyDictionary<DateOnly, string> Holidays { get; init; }
        public required HashSet<DateOnly> LeaveDates { get; init; }
        public required HashSet<DateOnly> PendingCorrectionDates { get; init; }
        public required DateOnly Today { get; init; }

        public int PendingCorrectionCount => PendingCorrectionDates.Count;

        /// <summary>Full day description: (status, shift, record, isWorkingDay, holidayName).</summary>
        public (AttendanceDayStatus Status, WorkShift? Shift, AttendanceDayRecord? Record, bool WorkingDay, string? HolidayName)
            Describe(DateOnly date)
        {
            var isHoliday = Holidays.TryGetValue(date, out var holidayName);
            var shift = Assignments.GetValueOrDefault(date);
            var working = !isHoliday && (shift is not null || (DefaultShift?.WeekDays ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Contains(AttendanceTimeHelper.WeekDayName(date)));

            if (isHoliday) return (AttendanceDayStatus.Holiday, shift, null, false, holidayName);
            if (!working) return (AttendanceDayStatus.WeekOff, null, null, false, null);
            if (LeaveDates.Contains(date)) return (AttendanceDayStatus.Leave, shift ?? DefaultShift, null, true, null);

            var effectiveShift = shift ?? DefaultShift;
            Records.TryGetValue(date, out var record);
            if (record is null)
                return (date < Today ? AttendanceDayStatus.Absent : AttendanceDayStatus.NotMarked, effectiveShift, null, true, null);
            if (record.CheckIn is null)
                return (date < Today ? AttendanceDayStatus.Absent : AttendanceDayStatus.NotMarked, effectiveShift, record, true, null);
            if (record.CheckOut is null)
                return (date < Today
                    ? AttendanceDayStatus.MissingPunch
                    : record.LateByMinutes > 0 ? AttendanceDayStatus.Late : AttendanceDayStatus.Present,
                    effectiveShift, record, true, null);

            var status = record.IsHalfDay
                ? AttendanceDayStatus.HalfDay
                : record.LateByMinutes > 0 ? AttendanceDayStatus.Late : AttendanceDayStatus.Present;
            return (status, effectiveShift, record, true, null);
        }

        public AttendanceDayDto ToDayDto(DateOnly date)
        {
            var (status, shift, record, _, _) = Describe(date);
            return new AttendanceDayDto(
                date,
                status,
                shift?.Name,
                record?.CheckIn,
                record?.CheckOut,
                record?.WorkedHours,
                record?.CheckIn is null ? null : record.LateByMinutes,
                record?.IsHalfDay ?? false,
                record?.Source.ToString(),
                PendingCorrectionDates.Contains(date) ? "Pending" : null);
        }
    }

    private async Task<CalendarData> BuildCalendarAsync(Employee employee, DateOnly from, DateOnly to,
        CancellationToken cancellationToken)
    {
        var today = AttendanceTimeHelper.DateLocal(_clock.UtcNow);

        var shifts = await _db.Set<WorkShift>().AsNoTracking()
            .Where(s => s.IsActive).ToListAsync(cancellationToken);
        var shiftById = shifts.ToDictionary(s => s.Id);
        var defaultShift = shifts.FirstOrDefault(s => s.IsDefault);

        var assignments = await _db.Set<ShiftAssignment>().AsNoTracking()
            .Where(a => a.EmployeeId == employee.Id && a.Date >= from && a.Date <= to)
            .ToListAsync(cancellationToken);
        var assignmentByDate = assignments
            .Where(a => shiftById.ContainsKey(a.ShiftId))
            .ToDictionary(a => a.Date, a => shiftById[a.ShiftId]);

        var records = await _db.Set<AttendanceDayRecord>().AsNoTracking()
            .Where(r => r.EmployeeId == employee.Id && r.WorkDate >= from && r.WorkDate <= to)
            .ToDictionaryAsync(r => r.WorkDate, cancellationToken);

        var holidays = await _db.Set<CompanyHoliday>().AsNoTracking()
            .Where(h => h.Date >= from && h.Date <= to && (h.LocationId == null || h.LocationId == employee.LocationId))
            .ToDictionaryAsync(h => h.Date, h => h.Name, cancellationToken);

        // Approved leave days win over punch states but never over holidays/week-offs.
        var overlappingLeave = await _db.Set<LeaveRequest>().AsNoTracking()
            .Where(r => r.EmployeeId == employee.Id && r.Status == LeaveRequestStatus.Approved
                        && r.StartDate <= to && r.EndDate >= from)
            .Select(r => new { r.StartDate, r.EndDate })
            .ToListAsync(cancellationToken);

        var leaveDates = new HashSet<DateOnly>();
        foreach (var range in overlappingLeave)
        {
            var start = range.StartDate < from ? from : range.StartDate;
            var end = range.EndDate > to ? to : range.EndDate;
            for (var date = start; date <= end; date = date.AddDays(1))
            {
                if (holidays.ContainsKey(date)) continue;
                if (!assignmentByDate.ContainsKey(date) && !(defaultShift?.WeekDays ?? string.Empty)
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Contains(AttendanceTimeHelper.WeekDayName(date))) continue;
                leaveDates.Add(date);
            }
        }

        var pendingCorrections = await _db.Set<AttendanceCorrectionRequest>().AsNoTracking()
            .Where(c => c.EmployeeId == employee.Id && c.Status == AttendanceCorrectionStatus.Pending
                        && c.WorkDate >= from && c.WorkDate <= to)
            .Select(c => c.WorkDate)
            .ToListAsync(cancellationToken);

        return new CalendarData
        {
            EmployeeId = employee.Id,
            From = from,
            To = to,
            DefaultShift = defaultShift,
            Assignments = assignmentByDate,
            Records = records,
            Holidays = holidays,
            LeaveDates = leaveDates,
            PendingCorrectionDates = pendingCorrections.ToHashSet(),
            Today = today
        };
    }

    // ---------------------------------------------------------------- helpers

    private static AttendanceSummaryDto Summarise(IReadOnlyList<AttendanceDayDto> days, int pendingCorrections)
    {
        var present = days.Count(d => d.Status is AttendanceDayStatus.Present or AttendanceDayStatus.Late
                                      or AttendanceDayStatus.MissingPunch);
        return new AttendanceSummaryDto(
            present,
            days.Count(d => d.Status == AttendanceDayStatus.Absent),
            days.Count(d => d.Status == AttendanceDayStatus.HalfDay),
            days.Count(d => d.Status == AttendanceDayStatus.Leave),
            days.Count(d => d.Status == AttendanceDayStatus.Holiday),
            days.Count(d => d.Status == AttendanceDayStatus.WeekOff),
            days.Count(d => d.Status == AttendanceDayStatus.Late),
            pendingCorrections);
    }

    private DateOnly ParseMonth(string? month)
    {
        if (string.IsNullOrWhiteSpace(month)) return AttendanceTimeHelper.DateLocal(_clock.UtcNow);
        if (DateOnly.TryParseExact(month.Trim(), "yyyy-MM", out var parsed))
            return AttendanceTimeHelper.FirstDayOfMonth(parsed);
        throw new RequestValidationException(errors: new() { ["month"] = ["Expected format YYYY-MM (e.g. 2026-10)."] });
    }

    private Guid RequireEmployeeId() =>
        _currentUser.EmployeeId
        ?? throw new ForbiddenException("Your user account is not linked to an employee record.");

    private bool CanViewTeam() =>
        _currentUser.HasPermission(Permissions.AttendanceViewTeam) ||
        _currentUser.HasPermission(Permissions.AttendanceManage);

    private async Task<Employee> ResolveTargetAsync(Guid? requestedEmployeeId, CancellationToken cancellationToken)
    {
        var mine = RequireEmployeeId();
        if (requestedEmployeeId is null || requestedEmployeeId == mine)
        {
            return await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == mine, cancellationToken)
                   ?? throw new NotFoundException();
        }

        if (!CanViewTeam()) throw new NotFoundException(); // scope: no existence leak
        return await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == requestedEmployeeId, cancellationToken)
               ?? throw new NotFoundException();
    }

    private static AttendanceSource ParseSource(string source) =>
        Enum.TryParse<AttendanceSource>(source, true, out var parsed) ? parsed : AttendanceSource.Mobile;
}
