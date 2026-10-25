using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Auditing;
using Wintech.Hrms.Api.Common.CurrentUser;
using Wintech.Hrms.Api.Common.ErrorHandling;
using Wintech.Hrms.Api.Common.Notifications;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Approvals;
using Wintech.Hrms.Api.Domain.Attendance;
using Wintech.Hrms.Api.Security;
using Wintech.Hrms.Api.Security.Auth;

namespace Wintech.Hrms.Api.Modules.Attendance;

public sealed class ShiftService : IShiftService
{
    private const int MaxRosterRangeDays = 92;

    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly IApprovalService _approvalService;
    private readonly INotificationPublisher _notifications;

    public ShiftService(AppDbContext db, ICurrentUser currentUser, IClock clock, IAuditService audit,
        IApprovalService approvalService, INotificationPublisher notifications)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
        _approvalService = approvalService;
        _notifications = notifications;
    }

    public async Task<IReadOnlyList<ShiftDto>> GetShiftsAsync(CancellationToken cancellationToken = default)
    {
        var shifts = await _db.Set<WorkShift>().AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.StartTime)
            .ToListAsync(cancellationToken);

        return shifts.Select(s => new ShiftDto(
            s.Id, s.Name, s.StartTime, s.EndTime, s.GraceMinutes, s.BreakMinutes,
            s.WeekDays.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))).ToList();
    }

    public async Task<IReadOnlyList<AdminShiftDto>> GetAdminShiftsAsync(CancellationToken cancellationToken = default)
    {
        EnsureShiftAdmin();
        var shifts = await _db.Set<WorkShift>().AsNoTracking()
            .OrderBy(s => s.StartTime).ThenBy(s => s.Code)
            .ToListAsync(cancellationToken);
        return shifts.Select(MapAdminShift).ToList();
    }

    public async Task<AdminShiftDto> UpsertShiftAsync(Guid? id, UpsertShiftRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureShiftAdmin();
        var code = request.Code.Trim();
        if (await _db.Set<WorkShift>().AnyAsync(shift => shift.Id != (id ?? Guid.Empty) &&
                shift.Code.ToLower() == code.ToLower(), cancellationToken))
            throw new ConflictException($"Shift code '{code}' is already in use.");
        if (request.EndTime == request.StartTime)
            throw new RequestValidationException("The shift times are invalid.",
                new() { ["endTime"] = ["A shift must have a non-zero duration; overnight shifts are supported."] });
        var validDays = new HashSet<string>(["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"], StringComparer.OrdinalIgnoreCase);
        var days = request.WeekDays.Select(day => day.Trim()).Where(day => day.Length > 0).ToList();
        if (days.Count == 0 || days.Any(day => !validDays.Contains(day)))
            throw new RequestValidationException("Shift weekdays are invalid.",
                new() { ["weekDays"] = ["Select one or more weekdays using Mon, Tue, Wed, Thu, Fri, Sat or Sun."] });

        var shift = id.HasValue
            ? await _db.Set<WorkShift>().FirstOrDefaultAsync(item => item.Id == id.Value, cancellationToken)
                ?? throw new NotFoundException("Shift not found.")
            : new WorkShift { Id = Guid.NewGuid() };
        var old = id.HasValue ? new { shift.Code, shift.Name, shift.IsActive, shift.IsDefault } : null;
        if (request.IsDefault)
        {
            var otherDefaults = await _db.Set<WorkShift>()
                .Where(item => item.Id != shift.Id && item.IsDefault)
                .ToListAsync(cancellationToken);
            foreach (var other in otherDefaults) other.IsDefault = false;
        }
        shift.Code = code;
        shift.Name = request.Name.Trim();
        shift.StartTime = request.StartTime;
        shift.EndTime = request.EndTime;
        shift.GraceMinutes = request.GraceMinutes;
        shift.BreakMinutes = request.BreakMinutes;
        shift.WeekDays = string.Join(',', days.Select(day => char.ToUpperInvariant(day[0]) + day[1..].ToLowerInvariant()).Distinct());
        shift.IsDefault = request.IsDefault;
        shift.IsActive = request.IsActive;
        if (!id.HasValue) _db.Add(shift);
        _audit.Add(id.HasValue ? "shift.update" : "shift.create", nameof(WorkShift), shift.Id.ToString(),
            details: new { old, current = new { shift.Code, shift.Name, shift.StartTime, shift.EndTime, shift.WeekDays, shift.IsDefault, shift.IsActive } });
        await _db.SaveChangesAsync(cancellationToken);
        return MapAdminShift(shift);
    }

    private void EnsureShiftAdmin()
    {
        _currentUser.EnsureActive();
        if (!_currentUser.HasPermission(Permissions.AdminConfig))
            throw new ForbiddenException("You do not have permission to configure shifts.");
    }

    private static AdminShiftDto MapAdminShift(WorkShift shift) => new(
        shift.Id,
        shift.Code,
        shift.Name,
        shift.StartTime,
        shift.EndTime,
        shift.GraceMinutes,
        shift.BreakMinutes,
        shift.WeekDays.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        shift.IsDefault,
        shift.IsActive);

    public async Task<IReadOnlyList<RosterEntryDto>> GetRosterAsync(DateOnly? from, DateOnly? to,
        CancellationToken cancellationToken = default)
    {
        var employeeId = RequireEmployeeId();
        var today = AttendanceTimeHelper.DateLocal(_clock.UtcNow);
        var start = from ?? today;
        var end = to ?? start.AddDays(30);
        if (end < start)
            throw new RequestValidationException(errors: new() { ["to"] = ["End date must be on or after start date."] });
        if (end.DayNumber - start.DayNumber > MaxRosterRangeDays)
            throw new RequestValidationException(errors: new() { ["to"] = [$"Range must not exceed {MaxRosterRangeDays} days."] });

        var shifts = await _db.Set<WorkShift>().AsNoTracking().Where(s => s.IsActive).ToListAsync(cancellationToken);
        var shiftById = shifts.ToDictionary(s => s.Id);
        var defaultShift = shifts.FirstOrDefault(s => s.IsDefault);

        var assignments = await _db.Set<ShiftAssignment>().AsNoTracking()
            .Where(a => a.EmployeeId == employeeId && a.Date >= start && a.Date <= end)
            .ToListAsync(cancellationToken);
        var assignmentByDate = assignments
            .Where(a => shiftById.ContainsKey(a.ShiftId))
            .ToDictionary(a => a.Date, a => shiftById[a.ShiftId]);

        var holidays = await _db.Set<CompanyHoliday>().AsNoTracking()
            .Where(h => h.Date >= start && h.Date <= end)
            .Select(h => h.Date)
            .ToListAsync(cancellationToken);
        var holidayDates = holidays.ToHashSet();

        var roster = new List<RosterEntryDto>();
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            if (holidayDates.Contains(date)) continue;
            var shift = assignmentByDate.GetValueOrDefault(date);
            if (shift is null)
            {
                var weekDays = (defaultShift?.WeekDays ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (defaultShift is null || !weekDays.Contains(AttendanceTimeHelper.WeekDayName(date))) continue;
                shift = defaultShift;
            }

            roster.Add(new RosterEntryDto(date, shift.Id, shift.Name, shift.StartTime, shift.EndTime));
        }

        return roster;
    }

    public async Task<ShiftSwapCreatedDto> CreateSwapRequestAsync(ShiftSwapRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var employeeId = RequireEmployeeId();
        var userId = _currentUser.RequireUserId();

        if (request.ProposedWithEmployeeId == employeeId)
            throw new BusinessRuleException(errors: new()
            {
                ["proposedWithEmployeeId"] = ["You cannot propose a shift swap with yourself."]
            });

        var myAssignment = await _db.Set<ShiftAssignment>().AsNoTracking()
            .FirstOrDefaultAsync(a => a.EmployeeId == employeeId && a.Date == request.RosterDate, cancellationToken);
        if (myAssignment is null)
            throw new BusinessRuleException("You have no rostered shift on the selected date.",
                new() { ["rosterDate"] = ["Pick a date from your published roster."] });

        var theirAssignment = await _db.Set<ShiftAssignment>().AsNoTracking()
            .FirstOrDefaultAsync(a => a.EmployeeId == request.ProposedWithEmployeeId && a.Date == request.RosterDate,
                cancellationToken);
        if (theirAssignment is null)
            throw new BusinessRuleException("The colleague you selected has no rostered shift on that date.",
                new() { ["proposedWithEmployeeId"] = ["They are not rostered on the selected date."] });

        if (request.ProposedShiftId is { } shiftId &&
            !await _db.Set<WorkShift>().AsNoTracking().AnyAsync(s => s.Id == shiftId && s.IsActive, cancellationToken))
            throw new NotFoundException("The proposed shift was not found.");

        var hasOpenRequest = await _db.Set<ShiftSwapRequest>().AsNoTracking()
            .AnyAsync(s => s.EmployeeId == employeeId && s.RosterDate == request.RosterDate
                           && s.Status == ShiftSwapStatus.Pending, cancellationToken);
        if (hasOpenRequest)
            throw new ConflictException("You already have a pending swap request for that date.");

        var requester = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);
        var colleague = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == request.ProposedWithEmployeeId, cancellationToken);

        var swap = new ShiftSwapRequest
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            RosterDate = request.RosterDate,
            ProposedWithEmployeeId = request.ProposedWithEmployeeId,
            ProposedShiftId = request.ProposedShiftId,
            Reason = request.Reason.Trim(),
            Status = ShiftSwapStatus.Pending,
            SubmittedAt = _clock.UtcNow,
            CreatedByUserId = userId
        };
        _db.Set<ShiftSwapRequest>().Add(swap);
        await _db.SaveChangesAsync(cancellationToken);

        var title = $"Shift swap on {request.RosterDate:yyyy-MM-dd} ({requester?.DisplayName ?? _currentUser.DisplayName})";
        var task = await _approvalService.StartAsync(new ApprovalStartRequest(
            "ShiftSwap", swap.Id, title, userId, employeeId, _currentUser.DisplayName), cancellationToken);

        swap.ApprovalTaskId = task.Id;
        _audit.Add("ShiftSwap.Request.Submitted", nameof(ShiftSwapRequest), swap.Id.ToString(), details: new
        {
            rosterDate = swap.RosterDate,
            proposedWith = colleague?.DisplayName,
            swap.ProposedShiftId,
            swap.Reason,
            approvalTaskId = task.Id
        });

        var colleagueUserId = await FindUserIdForEmployeeAsync(request.ProposedWithEmployeeId, cancellationToken);
        if (colleagueUserId is not null)
        {
            await _notifications.PublishAsync(new NotificationMessage(
                colleagueUserId, "ShiftSwapProposed",
                "A shift swap was proposed",
                $"{requester?.DisplayName ?? _currentUser.DisplayName} proposed to swap shifts with you on {request.RosterDate:yyyy-MM-dd}.",
                $"/shifts/swap-requests/{swap.Id}", "Attendance", EmployeeId: request.ProposedWithEmployeeId),
                cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new ShiftSwapCreatedDto(swap.Id, task.Id);
    }

    private Guid RequireEmployeeId() =>
        _currentUser.EmployeeId
        ?? throw new ForbiddenException("Your user account is not linked to an employee record.");

    private async Task<Guid?> FindUserIdForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var account = await _db.UserAccounts.AsNoTracking()
            .Where(u => u.EmployeeId == employeeId)
            .OrderBy(u => u.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return account?.Id;
    }
}

public sealed class HolidayService : IHolidayService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly Common.Time.IClock _clock;
    private readonly Common.Auditing.IAuditService _audit;

    public HolidayService(AppDbContext db, ICurrentUser currentUser, Common.Time.IClock clock,
        Common.Auditing.IAuditService audit)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
    }

    public async Task<IReadOnlyList<HolidayDto>> GetHolidaysAsync(int? year, Guid? locationId = null,
        CancellationToken cancellationToken = default)
    {
        var targetYear = year ?? AttendanceTimeHelper.DateLocal(_clock.UtcNow).Year;
        if (_currentUser.HasPermission(Permissions.AdminConfig))
        {
            if (locationId.HasValue && !await _db.Locations.AnyAsync(location =>
                    location.Id == locationId.Value, cancellationToken))
                throw new Common.ErrorHandling.RequestValidationException("The selected location does not exist.",
                    new() { ["locationId"] = ["Choose an existing location or leave blank for all locations."] });
        }
        else
        {
            // A normal employee cannot query holidays scoped to another location.
            locationId = await CurrentLocationIdAsync(cancellationToken);
        }
        var from = new DateOnly(targetYear, 1, 1);
        var to = new DateOnly(targetYear, 12, 31);

        var query = _db.Set<CompanyHoliday>().AsNoTracking()
            .Where(h => h.Date >= from && h.Date <= to);
        if (locationId.HasValue)
            query = query.Where(h => h.LocationId == null || h.LocationId == locationId);
        else if (!_currentUser.HasPermission(Permissions.AdminConfig))
            query = query.Where(h => h.LocationId == null);

        var holidays = await query.OrderBy(h => h.Date).ToListAsync(cancellationToken);
        return holidays.Select(h => new HolidayDto(h.Id, h.Date, h.Name, h.Type, h.LocationId)).ToList();
    }

    public async Task<HolidayDto> CreateHolidayAsync(UpsertHolidayRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.LocationId is { } locationId &&
            !await _db.Locations.AnyAsync(location => location.Id == locationId, cancellationToken))
            throw new Common.ErrorHandling.RequestValidationException("The selected location does not exist.",
                new() { ["locationId"] = ["Choose an existing location or leave blank for all locations."] });
        if (await _db.Set<CompanyHoliday>().AnyAsync(holiday => holiday.Date == request.Date &&
                holiday.LocationId == request.LocationId, cancellationToken))
            throw new Common.ErrorHandling.ConflictException("A holiday already exists for this date and location.");

        var holiday = new CompanyHoliday
        {
            Id = Guid.NewGuid(),
            Date = request.Date,
            Name = request.Name.Trim(),
            Type = request.Type,
            LocationId = request.LocationId
        };
        _db.Add(holiday);
        _audit.Add("holiday.create", nameof(CompanyHoliday), holiday.Id.ToString(), details: new
        {
            holiday.Date, holiday.Name, holiday.Type, holiday.LocationId
        });
        await _db.SaveChangesAsync(cancellationToken);
        return new HolidayDto(holiday.Id, holiday.Date, holiday.Name, holiday.Type, holiday.LocationId);
    }

    public async Task<HolidayDto> UpdateHolidayAsync(Guid id, UpsertHolidayRequest request,
        CancellationToken cancellationToken = default)
    {
        var holiday = await _db.Set<CompanyHoliday>().FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new Common.ErrorHandling.NotFoundException("Holiday not found.");
        if (request.LocationId is { } locationId &&
            !await _db.Locations.AnyAsync(location => location.Id == locationId, cancellationToken))
            throw new Common.ErrorHandling.RequestValidationException("The selected location does not exist.",
                new() { ["locationId"] = ["Choose an existing location or leave blank for all locations."] });
        if (await _db.Set<CompanyHoliday>().AnyAsync(item => item.Id != id &&
                item.Date == request.Date && item.LocationId == request.LocationId, cancellationToken))
            throw new Common.ErrorHandling.ConflictException("A holiday already exists for this date and location.");

        var old = new { holiday.Date, holiday.Name, holiday.Type, holiday.LocationId };
        holiday.Date = request.Date;
        holiday.Name = request.Name.Trim();
        holiday.Type = request.Type;
        holiday.LocationId = request.LocationId;
        _audit.Add("holiday.update", nameof(CompanyHoliday), id.ToString(), details: new
        {
            old,
            current = new { holiday.Date, holiday.Name, holiday.Type, holiday.LocationId }
        });
        await _db.SaveChangesAsync(cancellationToken);
        return new HolidayDto(holiday.Id, holiday.Date, holiday.Name, holiday.Type, holiday.LocationId);
    }

    private async Task<Guid?> CurrentLocationIdAsync(CancellationToken cancellationToken)
    {
        if (_currentUser.EmployeeId is not { } employeeId) return null;
        var employee = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);
        return employee?.LocationId;
    }
}
