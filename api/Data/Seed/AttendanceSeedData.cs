using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Registration;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Approvals;
using Wintech.Hrms.Api.Domain.Attendance;
using Wintech.Hrms.Api.Domain.Employees;
using Wintech.Hrms.Api.Domain.Leave;
using Wintech.Hrms.Api.Modules.Attendance;

namespace Wintech.Hrms.Api.Data.Seed;

/// <summary>
/// Idempotent attendance seed (Order 110): shifts, 2026 holidays (incl. 2026-11-01 Karnataka
/// Rajyotsava), a roster covering the current month, demo punch history for the current month
/// and one pending attendance correction so the approvals demo works out of the box.
/// </summary>
public sealed class AttendanceSeedData : IDataSeeder
{
    private readonly AppDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<AttendanceSeedData> _logger;

    public AttendanceSeedData(AppDbContext db, IClock clock, ILogger<AttendanceSeedData> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public int Order => 110;

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var shifts = await EnsureShiftsAsync(cancellationToken);
        await EnsureHolidaysAsync(cancellationToken);
        await EnsureRosterAndPunchesAsync(shifts, cancellationToken);
        await EnsurePendingCorrectionAsync(cancellationToken);
    }

    // ---------------------------------------------------------------- shifts

    private async Task<List<WorkShift>> EnsureShiftsAsync(CancellationToken cancellationToken)
    {
        var existing = await _db.Set<WorkShift>().AsNoTracking().ToListAsync(cancellationToken);
        if (existing.Count > 0) return existing;

        var general = new WorkShift
        {
            Id = Guid.NewGuid(), Code = "GENERAL", Name = "General",
            StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(18, 0),
            GraceMinutes = 15, BreakMinutes = 30, WeekDays = "Mon,Tue,Wed,Thu,Fri", IsDefault = true
        };
        _db.Set<WorkShift>().AddRange(
            general,
            new WorkShift
            {
                Id = Guid.NewGuid(), Code = "EARLY", Name = "Early Bird",
                StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(17, 0),
                GraceMinutes = 10, BreakMinutes = 30, WeekDays = "Mon,Tue,Wed,Thu,Fri"
            },
            new WorkShift
            {
                Id = Guid.NewGuid(), Code = "SUPPORT", Name = "Support",
                StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(19, 0),
                GraceMinutes = 15, BreakMinutes = 30, WeekDays = "Mon,Tue,Wed,Thu,Fri,Sat"
            },
            new WorkShift
            {
                Id = Guid.NewGuid(), Code = "NIGHT", Name = "Night",
                StartTime = new TimeOnly(22, 0), EndTime = new TimeOnly(7, 0),
                GraceMinutes = 10, BreakMinutes = 45, WeekDays = "Mon,Tue,Wed,Thu,Fri,Sat"
            });
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Seeded attendance shifts.");
        return await _db.Set<WorkShift>().AsNoTracking().ToListAsync(cancellationToken);
    }

    // ---------------------------------------------------------------- holidays

    private async Task EnsureHolidaysAsync(CancellationToken cancellationToken)
    {
        if (await _db.Set<CompanyHoliday>().AsNoTracking().AnyAsync(cancellationToken)) return;

        _db.Set<CompanyHoliday>().AddRange(
            Holiday(new DateOnly(2026, 1, 1), "New Year's Day", HolidayKind.Public),
            Holiday(new DateOnly(2026, 1, 14), "Makar Sankranti", HolidayKind.Optional),
            Holiday(new DateOnly(2026, 3, 4), "Holi", HolidayKind.Public),
            Holiday(new DateOnly(2026, 4, 3), "Good Friday", HolidayKind.Optional),
            Holiday(new DateOnly(2026, 5, 1), "May Day", HolidayKind.Optional),
            Holiday(new DateOnly(2026, 8, 15), "Independence Day", HolidayKind.Public),
            Holiday(new DateOnly(2026, 9, 14), "Ganesh Chaturthi", HolidayKind.Public),
            Holiday(new DateOnly(2026, 10, 2), "Gandhi Jayanti", HolidayKind.Public),
            Holiday(new DateOnly(2026, 10, 20), "Diwali", HolidayKind.Public),
            Holiday(new DateOnly(2026, 11, 1), "Karnataka Rajyotsava", HolidayKind.Public),
            Holiday(new DateOnly(2026, 12, 24), "Year-end shutdown (Day 1)", HolidayKind.Shutdown),
            Holiday(new DateOnly(2026, 12, 25), "Christmas", HolidayKind.Public));
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Seeded 2026 holiday calendar.");

        static CompanyHoliday Holiday(DateOnly date, string name, HolidayKind kind) => new()
        {
            Id = Guid.NewGuid(), Date = date, Name = name, Type = kind
        };
    }

    // ---------------------------------------------------------------- roster + punches

    private async Task EnsureRosterAndPunchesAsync(List<WorkShift> shifts, CancellationToken cancellationToken)
    {
        var employees = await _db.Employees.AsNoTracking()
            .Where(e => e.EmploymentStatus != EmploymentStatus.Terminated)
            .OrderBy(e => e.EmployeeNumber)
            .ToListAsync(cancellationToken);
        if (employees.Count == 0) return;

        var general = shifts.First(s => s.Code == "GENERAL");
        var today = AttendanceTimeHelper.DateLocal(_clock.UtcNow);
        var monthStart = AttendanceTimeHelper.FirstDayOfMonth(today);
        var monthEnd = AttendanceTimeHelper.LastDayOfMonth(monthStart);

        // ---- roster: every active employee, Mon-Sat of the current month (Sun = week off)
        if (!await _db.Set<ShiftAssignment>().AsNoTracking()
                .AnyAsync(a => a.Date >= monthStart && a.Date <= monthEnd, cancellationToken))
        {
            var roster = new List<ShiftAssignment>();
            for (var date = monthStart; date <= monthEnd; date = date.AddDays(1))
            {
                if (date.DayOfWeek == DayOfWeek.Sunday) continue;
                foreach (var employee in employees)
                {
                    roster.Add(new ShiftAssignment
                    {
                        Id = Guid.NewGuid(),
                        EmployeeId = employee.Id,
                        Date = date,
                        ShiftId = general.Id
                    });
                }
            }

            _db.Set<ShiftAssignment>().AddRange(roster);
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Seeded current-month roster ({Days} day(s)).", roster.Count / Math.Max(1, employees.Count));
        }

        // ---- demo punch history for the current month (EMP006/EMP007/EMP010, past days only)
        var holidayDates = (await _db.Set<CompanyHoliday>().AsNoTracking()
                .Select(h => h.Date).ToListAsync(cancellationToken)).ToHashSet();

        DateOnly? Emp007Leave(DateOnly date) =>
            date >= new DateOnly(2026, 10, 5) && date <= new DateOnly(2026, 10, 7) ? date : null;

        var demoEmployees = employees
            .Where(e => e.EmployeeNumber is "EMP006" or "EMP007" or "EMP010")
            .ToList();

        var records = new List<AttendanceDayRecord>();
        for (var date = monthStart; date < today; date = date.AddDays(1))
        {
            if (date.DayOfWeek == DayOfWeek.Sunday || holidayDates.Contains(date)) continue;

            foreach (var employee in demoEmployees)
            {
                if (employee.EmployeeNumber == "EMP007" && Emp007Leave(date) is not null) continue;
                if (await _db.Set<AttendanceDayRecord>().AsNoTracking()
                        .AnyAsync(r => r.EmployeeId == employee.Id && r.WorkDate == date, cancellationToken))
                    continue;

                var dayOfMonth = date.Day;
                var checkIn = TimeOnly.FromDateTime(new DateTime(date.Year, date.Month, date.Day,
                    9, dayOfMonth % 12, 0));
                var checkOut = TimeOnly.FromDateTime(new DateTime(date.Year, date.Month, date.Day,
                    18, (dayOfMonth * 7) % 12, 0));

                var late = false;
                TimeOnly? punchOut = checkOut;

                switch (employee.EmployeeNumber)
                {
                    case "EMP006" when date == new DateOnly(2026, 10, 21):
                        punchOut = null; // forgot to check out -> pending correction below
                        break;
                    case "EMP007" when dayOfMonth % 6 == 2:
                        checkIn = TimeOnly.FromDateTime(new DateTime(date.Year, date.Month, date.Day, 9, 32, 0));
                        late = true; // 17 min past grace (09:15)
                        break;
                    case "EMP010" when dayOfMonth % 8 == 4:
                        punchOut = null; // missing punch, no correction submitted
                        break;
                }

                records.Add(new AttendanceDayRecord
                {
                    Id = Guid.NewGuid(),
                    EmployeeId = employee.Id,
                    WorkDate = date,
                    ShiftId = general.Id,
                    CheckIn = checkIn,
                    CheckOut = punchOut,
                    LateByMinutes = late ? 17 : 0,
                    Status = punchOut is null
                        ? AttendanceDayStatus.MissingPunch
                        : late ? AttendanceDayStatus.Late : AttendanceDayStatus.Present,
                    WorkedHours = AttendanceTimeHelper.WorkedHours(checkIn, punchOut, general.BreakMinutes),
                    Source = AttendanceSource.Mobile,
                    UpdatedAt = _clock.UtcNow
                });
            }
        }

        if (records.Count > 0)
        {
            _db.Set<AttendanceDayRecord>().AddRange(records);
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Seeded {Count} demo attendance record(s) for the current month.", records.Count);
        }
    }

    // ---------------------------------------------------------------- pending correction

    private async Task EnsurePendingCorrectionAsync(CancellationToken cancellationToken)
    {
        if (await _db.Set<AttendanceCorrectionRequest>().AsNoTracking().AnyAsync(cancellationToken)) return;

        var priya = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.EmployeeNumber == "EMP006", cancellationToken);
        var record = priya is null
            ? null
            : await _db.Set<AttendanceDayRecord>().AsNoTracking()
                .FirstOrDefaultAsync(r => r.EmployeeId == priya.Id && r.WorkDate == new DateOnly(2026, 10, 21),
                    cancellationToken);
        if (priya is null || record is null) return;

        var requesterUserId = await _db.UserAccounts.AsNoTracking()
            .Where(u => u.EmployeeId == priya.Id)
            .OrderBy(u => u.Id)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (requesterUserId is null) return;
        var requesterGuid = requesterUserId.Value;

        var correction = new AttendanceCorrectionRequest
        {
            Id = Guid.NewGuid(),
            EmployeeId = priya.Id,
            WorkDate = record.WorkDate,
            Type = AttendanceCorrectionType.MissingPunch,
            ProposedCheckIn = record.CheckIn,
            ProposedCheckOut = new TimeOnly(18, 0),
            OriginalCheckIn = record.CheckIn,
            OriginalCheckOut = record.CheckOut,
            Reason = "Forgot to check out before leaving the office.",
            Status = AttendanceCorrectionStatus.Pending,
            SubmittedAt = _clock.UtcNow.AddDays(-2),
            CreatedByUserId = requesterGuid
        };

        var task = await CreateApprovalTaskAsync(_db, _clock, "AttendanceCorrection", correction.Id,
            $"MissingPunch correction for {correction.WorkDate:yyyy-MM-dd} ({priya.DisplayName})",
            requesterGuid, priya.Id, priya.DisplayName, pending: true, cancellationToken);

        correction.ApprovalTaskId = task.Id;
        _db.Set<AttendanceCorrectionRequest>().Add(correction);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded pending attendance correction for {Employee}.", priya.EmployeeNumber);
    }

    /// <summary>Creates a matching approval task (shared with the leave seed; no-op when present).</summary>
    internal static async Task<ApprovalTask> CreateApprovalTaskAsync(AppDbContext db, IClock clock,
        string requestType, Guid requestId, string title,
        Guid requesterUserId, Guid? requesterEmployeeId, string requesterName, bool pending,
        CancellationToken cancellationToken)
    {
        var existing = await db.Set<ApprovalTask>().AsNoTracking()
            .FirstOrDefaultAsync(t => t.RequestType == requestType && t.RequestId == requestId, cancellationToken);
        if (existing is not null) return existing;

        var workflow = await db.ApprovalWorkflowDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(w => w.RequestType == requestType && w.IsActive, cancellationToken);

        Guid? managerUserId = await db.UserAccounts.AsNoTracking()
            .Where(u => u.UserName == "manager")
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var task = new ApprovalTask
        {
            Id = Guid.NewGuid(),
            RequestType = requestType,
            RequestId = requestId,
            Title = title,
            RequesterUserId = requesterUserId,
            RequesterEmployeeId = requesterEmployeeId,
            RequesterName = requesterName,
            Status = pending ? nameof(ApprovalStatus.Pending) : nameof(ApprovalStatus.Approved),
            CurrentStepNumber = 1,
            CurrentStepName = "Line manager",
            CurrentAssigneeUserId = pending ? managerUserId : null,
            CurrentAssigneeRole = pending ? "LineManager" : null,
            WorkflowDefinitionId = workflow?.Id,
            WorkflowVersion = workflow?.Version ?? 1,
            CreatedAt = clock.UtcNow.AddDays(-2),
            UpdatedAt = clock.UtcNow.AddDays(-2),
            DueAt = pending ? clock.UtcNow.AddDays(2) : null,
            CompletedAt = pending ? null : clock.UtcNow
        };
        task.Actions.Add(new ApprovalAction
        {
            Id = Guid.NewGuid(),
            TaskId = task.Id,
            StepNumber = 1,
            Action = ApprovalActionType.Start,
            ActorUserId = requesterUserId,
            ActorName = requesterName,
            Comment = null,
            At = clock.UtcNow.AddDays(-2)
        });

        db.Set<ApprovalTask>().Add(task);
        return task;
    }
}
