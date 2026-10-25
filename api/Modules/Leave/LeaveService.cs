using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Auditing;
using Wintech.Hrms.Api.Common.Configuration;
using Wintech.Hrms.Api.Common.CurrentUser;
using Wintech.Hrms.Api.Common.ErrorHandling;
using Wintech.Hrms.Api.Common.Notifications;
using Wintech.Hrms.Api.Common.Paging;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Approvals;
using Wintech.Hrms.Api.Domain.Attendance;
using Wintech.Hrms.Api.Domain.Leave;
using Wintech.Hrms.Api.Security;

namespace Wintech.Hrms.Api.Modules.Leave;

public sealed class LeaveService : ILeaveService
{
    private static readonly Lazy<TimeZoneInfo> OrgZone = new(() =>
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromMinutes(330), "India Standard Time", "IST"); }
        catch (InvalidTimeZoneException) { return TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromMinutes(330), "India Standard Time", "IST"); }
    });

    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly IApprovalService _approvalService;
    private readonly INotificationPublisher _notifications;
    private readonly SecurityOptions _securityOptions;

    public LeaveService(AppDbContext db, ICurrentUser currentUser, IClock clock, IAuditService audit,
        IApprovalService approvalService, INotificationPublisher notifications, SecurityOptions securityOptions)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
        _approvalService = approvalService;
        _notifications = notifications;
        _securityOptions = securityOptions;
    }

    private DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, OrgZone.Value));

    // ---------------------------------------------------------------- reference data

    public async Task<IReadOnlyList<LeaveTypeDto>> GetTypesAsync(CancellationToken cancellationToken = default)
    {
        var types = await _db.Set<LeaveType>().AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.DisplayOrder).ThenBy(t => t.Code)
            .ToListAsync(cancellationToken);

        return types.Select(t => new LeaveTypeDto(t.Id, t.Code, t.Name, t.IsPaid, t.RequiresAttachment,
            t.AllowsHalfDay, t.MaxConsecutiveDays)).ToList();
    }

    public async Task<IReadOnlyList<LeaveBalanceDto>> GetBalancesAsync(int? year,
        CancellationToken cancellationToken = default)
    {
        var employeeId = RequireEmployeeId();
        var targetYear = year ?? Today.Year;

        var types = await _db.Set<LeaveType>().AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.DisplayOrder).ThenBy(t => t.Code)
            .ToListAsync(cancellationToken);
        var typeById = types.ToDictionary(t => t.Id);

        var balances = await _db.Set<LeaveBalance>().AsNoTracking()
            .Where(b => b.EmployeeId == employeeId && b.Year == targetYear)
            .ToListAsync(cancellationToken);

        var byType = balances.ToDictionary(b => b.LeaveTypeId);

        return types.Select(t =>
        {
            byType.TryGetValue(t.Id, out var balance);
            return new LeaveBalanceDto(
                t.Id, t.Code, t.Name,
                balance?.Available ?? 0,
                balance?.Accrued ?? 0,
                balance?.Used ?? 0,
                balance?.Pending ?? 0,
                balance?.CarryForward ?? 0,
                t.Unit);
        }).ToList();
    }

    public async Task<IReadOnlyList<LeavePolicyDto>> GetPoliciesAsync(CancellationToken cancellationToken = default)
    {
        var policies = await _db.Set<LeavePolicy>().AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Code)
            .ToListAsync(cancellationToken);

        return policies.Select(p => new LeavePolicyDto(p.Id, p.Code, p.Name, p.Description,
            p.AccrualFrequency.ToString(), p.AnnualEntitlementDays, p.CarryForwardMaxDays,
            p.NegativeBalanceAllowed, p.NoticeDays, p.AttachmentRequired)).ToList();
    }

    public async Task<IReadOnlyList<AdminLeaveTypeDto>> GetAdminLeaveTypesAsync(CancellationToken cancellationToken = default)
    {
        RequireAdminConfig();
        var items = await _db.Set<LeaveType>().AsNoTracking()
            .OrderBy(item => item.DisplayOrder).ThenBy(item => item.Code)
            .ToListAsync(cancellationToken);
        return items.Select(MapAdminLeaveType).ToList();
    }

    public async Task<IReadOnlyList<AdminLeavePolicyDto>> GetAdminLeavePoliciesAsync(CancellationToken cancellationToken = default)
    {
        RequireAdminConfig();
        var items = await _db.Set<LeavePolicy>().AsNoTracking()
            .OrderBy(item => item.Code)
            .ToListAsync(cancellationToken);
        return items.Select(MapAdminLeavePolicy).ToList();
    }

    public async Task<AdminLeaveTypeDto> UpsertLeaveTypeAsync(Guid? id, UpsertLeaveTypeRequest request,
        CancellationToken cancellationToken = default)
    {
        RequireAdminConfig();
        var code = request.Code.Trim();
        if (await _db.Set<LeaveType>().AnyAsync(item => item.Id != (id ?? Guid.Empty) &&
                item.Code.ToLower() == code.ToLower(), cancellationToken))
            throw new ConflictException($"Leave type code '{code}' is already in use.");
        var item = id.HasValue
            ? await _db.Set<LeaveType>().FirstOrDefaultAsync(row => row.Id == id.Value, cancellationToken)
                ?? throw new NotFoundException("Leave type not found.")
            : new LeaveType { Id = Guid.NewGuid() };
        var old = id.HasValue ? new { item.Code, item.Name, item.IsActive } : null;
        item.Code = code;
        item.Name = request.Name.Trim();
        item.IsPaid = request.IsPaid;
        item.RequiresAttachment = request.RequiresAttachment;
        item.AllowsHalfDay = request.AllowsHalfDay;
        item.MaxConsecutiveDays = request.MaxConsecutiveDays;
        item.Unit = request.Unit.Trim();
        item.DisplayOrder = request.DisplayOrder;
        item.IsActive = request.IsActive;
        if (!id.HasValue) _db.Add(item);
        _audit.Add(id.HasValue ? "leave_type.update" : "leave_type.create", nameof(LeaveType),
            item.Id.ToString(), details: new { old, current = new { item.Code, item.Name, item.IsActive } });
        await _db.SaveChangesAsync(cancellationToken);
        return MapAdminLeaveType(item);
    }

    public async Task<AdminLeavePolicyDto> UpsertLeavePolicyAsync(Guid? id, UpsertLeavePolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        RequireAdminConfig();
        var code = request.Code.Trim();
        if (await _db.Set<LeavePolicy>().AnyAsync(item => item.Id != (id ?? Guid.Empty) &&
                item.Code.ToLower() == code.ToLower(), cancellationToken))
            throw new ConflictException($"Leave policy code '{code}' is already in use.");
        if (request.LeaveTypeId.HasValue && !await _db.Set<LeaveType>().AnyAsync(item =>
                item.Id == request.LeaveTypeId.Value, cancellationToken))
            throw new RequestValidationException("The selected leave type does not exist.",
                new() { ["leaveTypeId"] = ["Choose an existing leave type or leave blank for all types."] });
        if (!Enum.TryParse<LeaveAccrualFrequency>(request.AccrualFrequency, true, out var frequency) ||
            !Enum.IsDefined(frequency))
            throw new RequestValidationException("Accrual frequency is invalid.",
                new() { ["accrualFrequency"] = ["Choose None, Monthly, Quarterly, Annual or OnDemand."] });
        var item = id.HasValue
            ? await _db.Set<LeavePolicy>().FirstOrDefaultAsync(row => row.Id == id.Value, cancellationToken)
                ?? throw new NotFoundException("Leave policy not found.")
            : new LeavePolicy { Id = Guid.NewGuid() };
        var old = id.HasValue ? new { item.Code, item.Name, item.AnnualEntitlementDays, item.IsActive } : null;
        item.Code = code;
        item.Name = request.Name.Trim();
        item.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        item.LeaveTypeId = request.LeaveTypeId;
        item.AccrualFrequency = frequency;
        item.AnnualEntitlementDays = request.AnnualEntitlementDays;
        item.CarryForwardMaxDays = request.CarryForwardMaxDays;
        item.NegativeBalanceAllowed = request.NegativeBalanceAllowed;
        item.NoticeDays = request.NoticeDays;
        item.AttachmentRequired = request.AttachmentRequired;
        item.IsActive = request.IsActive;
        if (!id.HasValue) _db.Add(item);
        _audit.Add(id.HasValue ? "leave_policy.update" : "leave_policy.create", nameof(LeavePolicy),
            item.Id.ToString(), details: new { old, current = new { item.Code, item.Name, item.AnnualEntitlementDays, item.IsActive } });
        await _db.SaveChangesAsync(cancellationToken);
        return MapAdminLeavePolicy(item);
    }

    private void RequireAdminConfig()
    {
        _currentUser.EnsureActive();
        if (!_currentUser.HasPermission(Permissions.AdminConfig))
            throw new ForbiddenException("You do not have permission to administer leave policies.");
    }

    private static AdminLeaveTypeDto MapAdminLeaveType(LeaveType item) => new(
        item.Id, item.Code, item.Name, item.IsPaid, item.RequiresAttachment, item.AllowsHalfDay,
        item.MaxConsecutiveDays, item.Unit, item.DisplayOrder, item.IsActive);

    private static AdminLeavePolicyDto MapAdminLeavePolicy(LeavePolicy item) => new(
        item.Id, item.Code, item.Name, item.Description, item.LeaveTypeId, item.AccrualFrequency.ToString(),
        item.AnnualEntitlementDays, item.CarryForwardMaxDays, item.NegativeBalanceAllowed,
        item.NoticeDays, item.AttachmentRequired, item.IsActive);

    // ---------------------------------------------------------------- submit

    public async Task<LeaveRequestCreatedDto> CreateRequestAsync(LeaveRequestCreateDto request,
        CancellationToken cancellationToken = default)
    {
        var employeeId = RequireEmployeeId();
        var userId = _currentUser.RequireUserId();
        var today = Today;

        if (request.EndDate < request.StartDate)
            throw new RequestValidationException(errors: new()
            {
                ["endDate"] = ["End date must be on or after start date."]
            });

        if (!Enum.TryParse<LeaveDayType>(request.DayType, true, out var dayType))
            throw new RequestValidationException(errors: new()
            {
                ["dayType"] = ["Expected Full, FirstHalf or SecondHalf."]
            });

        var leaveType = await _db.Set<LeaveType>().AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.LeaveTypeId && t.IsActive, cancellationToken)
            ?? throw new NotFoundException("The requested leave type was not found.");

        var policy = await FindPolicyAsync(leaveType.Id, cancellationToken);
        var halfDay = dayType != LeaveDayType.Full;

        // ---- half-day rules (FR-EMP-041)
        if (halfDay)
        {
            if (!leaveType.AllowsHalfDay)
                throw new BusinessRuleException($"{leaveType.Name} does not allow half-day requests.",
                    new() { ["dayType"] = [($"{leaveType.Name} must be booked as full days.")] });
            if (request.StartDate != request.EndDate)
                throw new BusinessRuleException("Half-day leave must start and end on the same date.",
                    new() { ["endDate"] = ["Half-day requests cover a single date."] });
            if (await IsNonWorkingDate(request.StartDate, employeeId, cancellationToken))
                throw new BusinessRuleException("The selected date is a weekend or company holiday.",
                    new() { ["startDate"] = ["Pick a working day."] });
        }

        // ---- maximum consecutive duration (FR-EMP-042)
        var spanDays = request.EndDate.DayNumber - request.StartDate.DayNumber + 1;
        if (leaveType.MaxConsecutiveDays is { } maxDays && spanDays > maxDays)
            throw new BusinessRuleException(
                $"{leaveType.Name} allows a maximum of {maxDays} consecutive day(s) per request.",
                new() { ["endDate"] = [$"Shorten the request to at most {maxDays} day(s)."] });

        // ---- working-day units, weekend/holiday-only rejection (FR-EMP-042)
        decimal units;
        if (halfDay)
        {
            units = 0.5m;
        }
        else
        {
            var workingDays = await CountWorkingDaysAsync(request.StartDate, request.EndDate, employeeId, cancellationToken);
            if (workingDays == 0)
                throw new BusinessRuleException(
                    "The requested dates contain only weekends or holidays.",
                    new() { ["startDate"] = ["No working day in the selected range."] });
            units = workingDays;
        }

        // ---- attachment rules (FR-ADM-050)
        if ((leaveType.RequiresAttachment || (policy?.AttachmentRequired ?? false)) &&
            string.IsNullOrWhiteSpace(request.AttachmentUrl))
            throw new BusinessRuleException(
                $"An attachment is required for {leaveType.Name}.",
                new() { ["attachmentUrl"] = ["Attach a supporting document and submit again."] });

        // ---- notice period (FR-ADM-050)
        if (policy is { NoticeDays: > 0 } && request.StartDate < today.AddDays(policy.NoticeDays))
            throw new BusinessRuleException(
                $"{leaveType.Name} requires {policy.NoticeDays} day(s) of notice.",
                new() { ["startDate"] = [$"The earliest start date is {today.AddDays(policy.NoticeDays):yyyy-MM-dd}."] });

        // ---- overlap with pending/approved requests (FR-EMP-042)
        var overlap = await _db.Set<LeaveRequest>().AsNoTracking()
            .FirstOrDefaultAsync(r => r.EmployeeId == employeeId
                                      && (r.Status == LeaveRequestStatus.Pending || r.Status == LeaveRequestStatus.Approved)
                                      && r.StartDate <= request.EndDate && r.EndDate >= request.StartDate,
                cancellationToken);
        if (overlap is not null)
            throw new BusinessRuleException("The requested dates overlap an existing request.",
                new()
                {
                    ["startDate"] = [$"Overlaps {(overlap.LeaveType != null ? overlap.LeaveType.Name : "a leave request")} " +
                                     $"({overlap.StartDate:yyyy-MM-dd} to {overlap.EndDate:yyyy-MM-dd}, {overlap.Status})."]
                });

        // ---- balance / negative-balance policy (FR-EMP-046)
        LeaveBalance? balance = await _db.Set<LeaveBalance>().FirstOrDefaultAsync(
            b => b.EmployeeId == employeeId && b.LeaveTypeId == leaveType.Id && b.Year == request.StartDate.Year,
            cancellationToken);

        var negativeAllowed = !leaveType.IsPaid || (policy?.NegativeBalanceAllowed ?? false);
        if (!negativeAllowed && balance is not null && balance.Available < units)
            throw new BusinessRuleException(
                $"Insufficient {leaveType.Name} balance.",
                new()
                {
                    ["leaveTypeId"] = [$"Available {balance.Available:0.##} day(s), requested {units:0.##} day(s)."]
                });
        if (!negativeAllowed && balance is null && units > 0)
            throw new BusinessRuleException(
                $"No {leaveType.Name} balance is configured for {request.StartDate.Year}.",
                new() { ["leaveTypeId"] = ["Contact HR to have a balance established."] });

        // ---- persist + start approval (201 with approvalTaskId)
        var leaveRequest = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            LeaveTypeId = leaveType.Id,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            DayType = dayType,
            Units = units,
            Reason = request.Reason.Trim(),
            AttachmentUrl = request.AttachmentUrl,
            Status = LeaveRequestStatus.Pending,
            SubmittedAt = _clock.UtcNow,
            CreatedByUserId = userId
        };
        _db.Set<LeaveRequest>().Add(leaveRequest);

        if (balance is null)
        {
            balance = new LeaveBalance
            {
                Id = Guid.NewGuid(),
                EmployeeId = employeeId,
                LeaveTypeId = leaveType.Id,
                Year = request.StartDate.Year
            };
            _db.Set<LeaveBalance>().Add(balance);
        }

        balance.Pending += units;
        await _db.SaveChangesAsync(cancellationToken);

        var title = $"{leaveType.Name} ({units:0.##} day(s)) {request.StartDate:dd MMM} to {request.EndDate:dd MMM}";
        var task = await _approvalService.StartAsync(new ApprovalStartRequest(
            "Leave", leaveRequest.Id, title, userId, employeeId, _currentUser.DisplayName), cancellationToken);

        leaveRequest.ApprovalTaskId = task.Id;
        _audit.Add("Leave.Request.Submitted", nameof(LeaveRequest), leaveRequest.Id.ToString(), details: new
        {
            leaveType = leaveType.Code,
            leaveRequest.StartDate,
            leaveRequest.EndDate,
            leaveRequest.DayType,
            units,
            leaveRequest.Reason,
            approvalTaskId = task.Id
        });

        var managerUserId = await FindManagerUserIdAsync(employeeId, cancellationToken);
        if (managerUserId is not null)
        {
            await _notifications.PublishAsync(new NotificationMessage(
                managerUserId, "LeaveRequested",
                "New leave request awaiting approval",
                $"{_currentUser.DisplayName} requested {leaveType.Name} ({units:0.##} day(s)) from {request.StartDate:yyyy-MM-dd} to {request.EndDate:yyyy-MM-dd}.",
                $"/leave/requests/{leaveRequest.Id}", "Leave", EmployeeId: employeeId), cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new LeaveRequestCreatedDto(leaveRequest.Id, leaveRequest.Status, units, task.Id);
    }

    // ---------------------------------------------------------------- list / detail

    public async Task<PagedResult<LeaveRequestListItem>> ListRequestsAsync(string? status, int? year, int? month,
        bool team, Guid? employeeId, PageRequest page, CancellationToken cancellationToken = default)
    {
        var scope = await ResolveScopeAsync(team, employeeId, cancellationToken);

        LeaveRequestStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<LeaveRequestStatus>(status, true, out var parsed))
                throw new RequestValidationException(errors: new() { ["status"] = ["Unknown status value."] });
            statusFilter = parsed;
        }

        var query = _db.Set<LeaveRequest>().AsNoTracking();
        query = scope.All
            ? query
            : scope.EmployeeIds.Count == 0
                ? query.Where(r => false)
                : query.Where(r => scope.EmployeeIds.Contains(r.EmployeeId));

        if (statusFilter is not null)
        {
            var value = statusFilter.Value;
            query = query.Where(r => r.Status == value);
        }

        if (year is { } y)
        {
            var yearStart = new DateOnly(y, 1, 1);
            var yearEnd = new DateOnly(y, 12, 31);
            query = query.Where(r => r.StartDate <= yearEnd && r.EndDate >= yearStart);
        }

        if (month is { } m && m is >= 1 and <= 12)
        {
            var yBase = year ?? Today.Year;
            var monthStart = new DateOnly(yBase, m, 1);
            var monthEnd = new DateOnly(yBase, m, DateTime.DaysInMonth(yBase, m));
            query = query.Where(r => r.StartDate <= monthEnd && r.EndDate >= monthStart);
        }

        var paged = await query
            .OrderByDescending(r => r.StartDate)
            .ThenByDescending(r => r.SubmittedAt)
            .ToPagedAsync(page, r => new
            {
                r.Id, r.LeaveTypeId, LeaveTypeName = r.LeaveType != null ? r.LeaveType.Name : "",
                r.StartDate, r.EndDate, r.Units, r.DayType, r.Status, r.Reason,
                r.SubmittedAt, r.DecidedAt, r.ApproverName, r.RejectionReason, r.ApprovalTaskId
            }, cancellationToken);

        var approverNames = await ResolveApproverNamesAsync(paged.Items.Select(i => i.ApprovalTaskId), cancellationToken);

        var items = paged.Items.Select(i => new LeaveRequestListItem(
            i.Id, i.LeaveTypeName, i.LeaveTypeId, i.StartDate, i.EndDate, i.Units, i.DayType, i.Status,
            i.Reason, i.SubmittedAt, i.DecidedAt,
            i.ApproverName ?? approverNames.GetValueOrDefault(i.ApprovalTaskId ?? Guid.Empty),
            i.RejectionReason, i.ApprovalTaskId)).ToList();

        return PagedResult<LeaveRequestListItem>.Create(items, paged.Page, paged.PageSize, paged.Total);
    }

    public async Task<LeaveRequestDetail> GetRequestAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var leaveRequest = await _db.Set<LeaveRequest>().AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (leaveRequest is null || !InScope(leaveRequest.EmployeeId))
            throw new NotFoundException();

        var approverNames = await ResolveApproverNamesAsync([leaveRequest.ApprovalTaskId], cancellationToken);
        var history = await BuildDecisionHistoryAsync(leaveRequest.ApprovalTaskId, cancellationToken);

        return new LeaveRequestDetail(
            leaveRequest.Id,
            leaveRequest.LeaveType != null ? leaveRequest.LeaveType.Name : "",
            leaveRequest.LeaveTypeId,
            leaveRequest.StartDate, leaveRequest.EndDate, leaveRequest.Units, leaveRequest.DayType,
            leaveRequest.Status, leaveRequest.Reason, leaveRequest.AttachmentUrl,
            leaveRequest.SubmittedAt, leaveRequest.DecidedAt,
            leaveRequest.ApproverName ?? approverNames.GetValueOrDefault(leaveRequest.ApprovalTaskId ?? Guid.Empty),
            leaveRequest.RejectionReason, leaveRequest.ApprovalTaskId, history);
    }

    // ---------------------------------------------------------------- cancel

    public async Task<LeaveCancelledDto> CancelRequestAsync(Guid id, string reason,
        CancellationToken cancellationToken = default)
    {
        var leaveRequest = await _db.Set<LeaveRequest>().AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (leaveRequest is null || leaveRequest.EmployeeId != _currentUser.EmployeeId)
            throw new NotFoundException(); // only the requester may cancel; no existence leak

        if (leaveRequest.Status != LeaveRequestStatus.Pending)
            throw new ConflictException(
                $"This request is {leaveRequest.Status} and can no longer be cancelled. Pending requests only.");

        var windowDays = Math.Max(0, _securityOptions.LeaveCancellationWindowDays);
        var lastCancellableDay = leaveRequest.StartDate.AddDays(-windowDays);
        if (Today > lastCancellableDay)
            throw new ConflictException(
                $"The cancellation window has passed (cancel at least {windowDays} day(s) before the leave starts).");

        // Cancels the approval task, which invokes LeaveApprovalHandler.OnCancelledAsync:
        // status -> Cancelled and the pending balance is released exactly once.
        await _approvalService.CancelAsync("Leave", leaveRequest.Id, reason, cancellationToken);

        _audit.Add("Leave.Request.CancelRequested", nameof(LeaveRequest), leaveRequest.Id.ToString(),
            details: new { leaveRequest.StartDate, leaveRequest.EndDate, reason });

        var status = await _db.Set<LeaveRequest>().AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => r.Status)
            .FirstOrDefaultAsync(cancellationToken);

        return new LeaveCancelledDto(status.ToString());
    }

    // ---------------------------------------------------------------- calendar

    public async Task<IReadOnlyList<LeaveCalendarEntry>> GetCalendarAsync(DateOnly from, DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var employeeId = RequireEmployeeId();
        if (to < from)
            throw new RequestValidationException(errors: new() { ["to"] = ["End date must be on or after start date."] });
        if (to.DayNumber - from.DayNumber > 366)
            throw new RequestValidationException(errors: new() { ["to"] = ["Range must not exceed one year."] });

        var employee = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);

        var holidays = await _db.Set<CompanyHoliday>().AsNoTracking()
            .Where(h => h.Date >= from && h.Date <= to
                        && (h.LocationId == null || h.LocationId == employee!.LocationId))
            .ToDictionaryAsync(h => h.Date, h => h.Name, cancellationToken);

        var assignments = await _db.Set<ShiftAssignment>().AsNoTracking()
            .Where(a => a.EmployeeId == employeeId && a.Date >= from && a.Date <= to)
            .Select(a => a.Date)
            .ToListAsync(cancellationToken);
        var assignedDates = assignments.ToHashSet();

        var defaultShift = await _db.Set<WorkShift>().AsNoTracking()
            .FirstOrDefaultAsync(s => s.IsActive && s.IsDefault, cancellationToken);
        var defaultDays = (defaultShift?.WeekDays ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet();

        var leaveRequests = await _db.Set<LeaveRequest>().AsNoTracking()
            .Include(r => r.LeaveType)
            .Where(r => r.EmployeeId == employeeId
                        && (r.Status == LeaveRequestStatus.Pending || r.Status == LeaveRequestStatus.Approved)
                        && r.StartDate <= to && r.EndDate >= from)
            .ToListAsync(cancellationToken);

        var entries = new List<LeaveCalendarEntry>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            if (holidays.TryGetValue(date, out var holidayName))
            {
                entries.Add(new LeaveCalendarEntry(date, "Holiday", holidayName, null));
                continue;
            }

            var working = assignedDates.Contains(date) ||
                          defaultDays.Contains(date.DayOfWeek switch
                          {
                              DayOfWeek.Monday => "Mon", DayOfWeek.Tuesday => "Tue",
                              DayOfWeek.Wednesday => "Wed", DayOfWeek.Thursday => "Thu",
                              DayOfWeek.Friday => "Fri", DayOfWeek.Saturday => "Sat", _ => "Sun"
                          });

            var leave = leaveRequests.FirstOrDefault(r => date >= r.StartDate && date <= r.EndDate);
            if (leave is not null)
            {
                entries.Add(new LeaveCalendarEntry(date, "Leave",
                    leave.LeaveType != null ? leave.LeaveType.Name : "Leave", leave.Status));
                continue;
            }

            if (!working)
                entries.Add(new LeaveCalendarEntry(date, "WeekOff", "Week off", null));
        }

        return entries;
    }

    // ---------------------------------------------------------------- helpers

    private async Task<LeavePolicy?> FindPolicyAsync(Guid leaveTypeId, CancellationToken cancellationToken)
    {
        var policies = await _db.Set<LeavePolicy>().AsNoTracking()
            .Where(p => p.IsActive)
            .ToListAsync(cancellationToken);
        return policies.FirstOrDefault(p => p.LeaveTypeId == leaveTypeId) ?? policies.FirstOrDefault();
    }

    private async Task<decimal> CountWorkingDaysAsync(DateOnly from, DateOnly to, Guid employeeId,
        CancellationToken cancellationToken)
    {
        var holidayDates = (await _db.Set<CompanyHoliday>().AsNoTracking()
                .Where(h => h.Date >= from && h.Date <= to)
                .Select(h => h.Date)
                .ToListAsync(cancellationToken)).ToHashSet();

        var assignedDates = (await _db.Set<ShiftAssignment>().AsNoTracking()
                .Where(a => a.EmployeeId == employeeId && a.Date >= from && a.Date <= to)
                .Select(a => a.Date)
                .ToListAsync(cancellationToken)).ToHashSet();

        var defaultShift = await _db.Set<WorkShift>().AsNoTracking()
            .FirstOrDefaultAsync(s => s.IsActive && s.IsDefault, cancellationToken);
        var defaultDays = (defaultShift?.WeekDays ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet();

        decimal count = 0;
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            if (holidayDates.Contains(date)) continue;
            var dayName = date.DayOfWeek switch
            {
                DayOfWeek.Monday => "Mon", DayOfWeek.Tuesday => "Tue",
                DayOfWeek.Wednesday => "Wed", DayOfWeek.Thursday => "Thu",
                DayOfWeek.Friday => "Fri", DayOfWeek.Saturday => "Sat", _ => "Sun"
            };
            if (assignedDates.Contains(date) || defaultDays.Contains(dayName)) count++;
        }

        return count;
    }

    private async Task<bool> IsNonWorkingDate(DateOnly date, Guid employeeId, CancellationToken cancellationToken)
        => await CountWorkingDaysAsync(date, date, employeeId, cancellationToken) == 0;

    private async Task<Guid?> FindManagerUserIdAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var managerId = await _db.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId)
            .Select(e => e.ManagerId)
            .FirstOrDefaultAsync(cancellationToken);
        if (managerId is null) return null;
        return await _db.UserAccounts.AsNoTracking()
            .Where(u => u.EmployeeId == managerId)
            .OrderBy(u => u.Id)
            .Select(u => u.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private sealed record Scope(bool All, List<Guid> EmployeeIds);

    private bool HasApprovePermission() =>
        _currentUser.HasPermission(Permissions.LeaveApprove) ||
        _currentUser.HasPermission(Permissions.AttendanceViewTeam);

    private Guid RequireEmployeeId() =>
        _currentUser.EmployeeId
        ?? throw new ForbiddenException("Your user account is not linked to an employee record.");

    private bool InScope(Guid employeeId)
    {
        if (employeeId == _currentUser.EmployeeId) return true;
        return HasApprovePermission() || _currentUser.HasPermission(Permissions.LeaveManage);
    }

    private async Task<Scope> ResolveScopeAsync(bool team, Guid? employeeId, CancellationToken cancellationToken)
    {
        var mine = RequireEmployeeId();

        // SuperAdmin may review leave requests across the tenant, not only direct reports.
        if (_currentUser.HasPermission(Permissions.All))
        {
            if (employeeId is not { } adminTarget) return new Scope(true, []);
            var exists = await _db.Employees.AsNoTracking()
                .AnyAsync(e => e.Id == adminTarget, cancellationToken);
            if (!exists) throw new NotFoundException();
            return new Scope(false, [adminTarget]);
        }

        if (team)
        {
            if (!HasApprovePermission() && !_currentUser.HasPermission(Permissions.LeaveManage))
                throw new ForbiddenException("The team filter requires leave.approve or attendance.view.team.");

            var reports = await _db.Employees.AsNoTracking()
                .Where(e => e.ManagerId == mine)
                .Select(e => e.Id)
                .ToListAsync(cancellationToken);

            if (employeeId is { } requested && requested != mine)
            {
                if (!reports.Contains(requested))
                {
                    var exists = await _db.Employees.AsNoTracking().AnyAsync(e => e.Id == requested, cancellationToken);
                    if (!exists) throw new NotFoundException();
                    throw new ForbiddenException("That employee is not on your team.");
                }
                return new Scope(false, [requested]);
            }

            reports.Add(mine);
            return new Scope(false, reports);
        }

        if (employeeId is null || employeeId == mine) return new Scope(false, [mine]);
        if (!InScope(employeeId.Value)) throw new NotFoundException(); // scope: no existence leak
        var found = await _db.Employees.AsNoTracking().AnyAsync(e => e.Id == employeeId, cancellationToken);
        if (!found) throw new NotFoundException();
        return new Scope(false, [employeeId.Value]);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> ResolveApproverNamesAsync(
        IEnumerable<Guid?> taskIds, CancellationToken cancellationToken)
    {
        var ids = taskIds.Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        var result = new Dictionary<Guid, string>();
        if (ids.Count == 0) return result;

        var tasks = await _db.Set<ApprovalTask>().AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .ToListAsync(cancellationToken);

        var assigneeIds = tasks.Where(t => t.CurrentAssigneeUserId is not null)
            .Select(t => t.CurrentAssigneeUserId!.Value).Distinct().ToList();
        var assigneeNames = new Dictionary<Guid, string>();
        if (assigneeIds.Count > 0)
        {
            var users = await _db.UserAccounts.AsNoTracking()
                .Where(u => assigneeIds.Contains(u.Id))
                .Select(u => new { u.Id, Name = u.Employee != null ? u.Employee.DisplayName : u.UserName })
                .ToListAsync(cancellationToken);
            assigneeNames = users.ToDictionary(u => u.Id, u => u.Name);
        }

        var completedIds = tasks.Where(t => t.Status != nameof(ApprovalStatus.Pending)).Select(t => t.Id).ToList();
        var lastActors = new Dictionary<Guid, string>();
        if (completedIds.Count > 0)
        {
            var actions = await _db.Set<ApprovalAction>().AsNoTracking()
                .Where(a => completedIds.Contains(a.TaskId))
                .OrderBy(a => a.At)
                .ToListAsync(cancellationToken);
            foreach (var action in actions)
            {
                if (action.Action is ApprovalActionType.Approve or ApprovalActionType.Reject or ApprovalActionType.Return)
                    lastActors[action.TaskId] = action.ActorName;
            }
        }

        foreach (var task in tasks)
        {
            var name = task.Status == nameof(ApprovalStatus.Pending)
                ? (task.CurrentAssigneeUserId is { } assigneeId ? assigneeNames.GetValueOrDefault(assigneeId) : null)
                : lastActors.GetValueOrDefault(task.Id);
            if (!string.IsNullOrWhiteSpace(name)) result[task.Id] = name;
        }

        return result;
    }

    private async Task<IReadOnlyList<LeaveDecisionHistoryEntry>> BuildDecisionHistoryAsync(Guid? taskId,
        CancellationToken cancellationToken)
    {
        if (taskId is null) return [];

        var actions = await _db.Set<ApprovalAction>().AsNoTracking()
            .Where(a => a.TaskId == taskId.Value)
            .OrderBy(a => a.At)
            .ToListAsync(cancellationToken);

        var stepNames = new Dictionary<int, string>();
        var task = await _db.Set<ApprovalTask>().AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == taskId.Value, cancellationToken);
        if (task?.WorkflowDefinitionId is { } workflowId)
        {
            var steps = await _db.Set<ApprovalWorkflowStep>().AsNoTracking()
                .Where(s => s.WorkflowId == workflowId)
                .ToListAsync(cancellationToken);
            foreach (var step in steps) stepNames[step.StepNumber] = step.Name;
        }

        return actions.Select(a => new LeaveDecisionHistoryEntry(
            stepNames.GetValueOrDefault(a.StepNumber, $"Step {a.StepNumber}"),
            a.Action.ToString(), a.ActorName, a.Comment, a.At)).ToList();
    }
}
