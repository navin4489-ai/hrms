using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Registration;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Approvals;
using Wintech.Hrms.Api.Domain.Employees;
using Wintech.Hrms.Api.Domain.Leave;

namespace Wintech.Hrms.Api.Data.Seed;

/// <summary>
/// Idempotent leave seed (Order 111): leave types (CL/SL/EL/LWP), their policies, 2026 balances
/// for all 12 seeded employees (consistent with the demo requests) and a handful of
/// pending/approved/rejected leave requests, each with its approval task.
/// </summary>
public sealed class LeaveSeedData : IDataSeeder
{
    private readonly AppDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<LeaveSeedData> _logger;

    public LeaveSeedData(AppDbContext db, IClock clock, ILogger<LeaveSeedData> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public int Order => 111;

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var types = await EnsureTypesAsync(cancellationToken);
        await EnsurePoliciesAsync(types, cancellationToken);
        await EnsureBalancesAsync(types, cancellationToken);
        await EnsureDemoRequestsAsync(types, cancellationToken);
    }

    // ---------------------------------------------------------------- types

    private async Task<Dictionary<string, LeaveType>> EnsureTypesAsync(CancellationToken cancellationToken)
    {
        var existing = await _db.Set<LeaveType>().AsNoTracking().ToListAsync(cancellationToken);
        if (existing.Count > 0) return existing.ToDictionary(t => t.Code, StringComparer.Ordinal);

        var types = new[]
        {
            new LeaveType
            {
                Id = Guid.NewGuid(), Code = "CL", Name = "Casual Leave", IsPaid = true,
                RequiresAttachment = false, AllowsHalfDay = true, MaxConsecutiveDays = 5, DisplayOrder = 1
            },
            new LeaveType
            {
                Id = Guid.NewGuid(), Code = "SL", Name = "Sick Leave", IsPaid = true,
                RequiresAttachment = true, AllowsHalfDay = true, MaxConsecutiveDays = 10, DisplayOrder = 2
            },
            new LeaveType
            {
                Id = Guid.NewGuid(), Code = "EL", Name = "Earned Leave", IsPaid = true,
                RequiresAttachment = false, AllowsHalfDay = true, MaxConsecutiveDays = 15, DisplayOrder = 3
            },
            new LeaveType
            {
                Id = Guid.NewGuid(), Code = "LWP", Name = "Leave Without Pay", IsPaid = false,
                RequiresAttachment = false, AllowsHalfDay = true, MaxConsecutiveDays = null, DisplayOrder = 4
            }
        };
        _db.Set<LeaveType>().AddRange(types);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Seeded {Count} leave types.", types.Length);
        return types.ToDictionary(t => t.Code, StringComparer.Ordinal);
    }

    // ---------------------------------------------------------------- policies

    private async Task EnsurePoliciesAsync(Dictionary<string, LeaveType> types, CancellationToken cancellationToken)
    {
        if (await _db.Set<LeavePolicy>().AsNoTracking().AnyAsync(cancellationToken)) return;

        LeavePolicy Policy(string code, string name, string description, string typeCode,
            LeaveAccrualFrequency frequency, decimal entitlement, decimal carryForwardMax,
            bool negativeAllowed, int noticeDays, bool attachmentRequired) => new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            Description = description,
            LeaveTypeId = types[typeCode].Id,
            AccrualFrequency = frequency,
            AnnualEntitlementDays = entitlement,
            CarryForwardMaxDays = carryForwardMax,
            NegativeBalanceAllowed = negativeAllowed,
            NoticeDays = noticeDays,
            AttachmentRequired = attachmentRequired,
            IsActive = true
        };

        _db.Set<LeavePolicy>().AddRange(
            Policy("LP-CL", "Casual Leave Policy",
                "12 days per year accrued monthly; up to 5 days carry forward into the next year; no negative balance.",
                "CL", LeaveAccrualFrequency.Monthly, 12m, 5m, negativeAllowed: false, noticeDays: 0, attachmentRequired: false),
            Policy("LP-SL", "Sick Leave Policy",
                "8 days per year; medical note required; negative balance permitted with HR approval.",
                "SL", LeaveAccrualFrequency.OnDemand, 8m, 0m, negativeAllowed: true, noticeDays: 0, attachmentRequired: true),
            Policy("LP-EL", "Earned Leave Policy",
                "12 days per year accrued monthly; up to 6 days carry forward; notice requested where possible.",
                "EL", LeaveAccrualFrequency.Monthly, 12m, 6m, negativeAllowed: false, noticeDays: 0, attachmentRequired: false),
            Policy("LP-LWP", "Leave Without Pay Policy",
                "Unpaid absence; entitlement 0, always permitted subject to approval.",
                "LWP", LeaveAccrualFrequency.None, 0m, 0m, negativeAllowed: true, noticeDays: 0, attachmentRequired: false));
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Seeded leave policies.");
    }

    // ---------------------------------------------------------------- balances (2026)

    private async Task EnsureBalancesAsync(Dictionary<string, LeaveType> types, CancellationToken cancellationToken)
    {
        const int year = 2026;
        if (await _db.Set<LeaveBalance>().AsNoTracking().AnyAsync(b => b.Year == year, cancellationToken)) return;

        var employees = await _db.Employees.AsNoTracking()
            .Where(e => e.EmploymentStatus != EmploymentStatus.Terminated)
            .OrderBy(e => e.EmployeeNumber)
            .ToListAsync(cancellationToken);
        if (employees.Count == 0) return;

        var balances = new List<LeaveBalance>();
        foreach (var employee in employees)
        {
            // CL: 10 accrued, 2 used (EMP007 has 3 approved days), pending matches seeded requests.
            balances.Add(Balance(employee.Id, "CL", year, accrued: 10m, used: employee.EmployeeNumber == "EMP007" ? 3m : 2m,
                pending: employee.EmployeeNumber == "EMP006" ? 2m : 0m, carryForward: 0m));
            // SL: 6 accrued, 1 used, pending matches the seeded SL request for EMP010.
            balances.Add(Balance(employee.Id, "SL", year, accrued: 6m, used: 1m,
                pending: employee.EmployeeNumber == "EMP010" ? 2m : 0m, carryForward: 0m));
            // EL: 8 accrued + 2 carried forward, 1 used (EMP009's rejected request is not counted).
            balances.Add(Balance(employee.Id, "EL", year, accrued: 8m, used: 1m, pending: 0m, carryForward: 2m));
            // LWP: no entitlement.
            balances.Add(Balance(employee.Id, "LWP", year, accrued: 0m, used: 0m, pending: 0m, carryForward: 0m));
        }

        _db.Set<LeaveBalance>().AddRange(balances);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded {Count} 2026 leave balance row(s).", balances.Count);

        LeaveBalance Balance(Guid employeeId, string code, int targetYear, decimal accrued, decimal used,
            decimal pending, decimal carryForward) => new()
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            LeaveTypeId = types[code].Id,
            Year = targetYear,
            Accrued = accrued,
            Used = used,
            Pending = pending,
            CarryForward = carryForward
        };
    }

    // ---------------------------------------------------------------- demo requests

    private async Task EnsureDemoRequestsAsync(Dictionary<string, LeaveType> types, CancellationToken cancellationToken)
    {
        if (await _db.Set<LeaveRequest>().AsNoTracking().AnyAsync(cancellationToken)) return;

        var employees = await _db.Employees.AsNoTracking()
            .OrderBy(e => e.EmployeeNumber)
            .ToDictionaryAsync(e => e.EmployeeNumber, cancellationToken);
        if (!employees.ContainsKey("EMP006")) return;

        var userByUserName = await _db.UserAccounts.AsNoTracking()
            .ToDictionaryAsync(u => u.UserName, StringComparer.Ordinal, cancellationToken);
        var fallbackUserId = userByUserName.GetValueOrDefault("admin")?.Id
                             ?? userByUserName.Values.FirstOrDefault()?.Id
                             ?? Guid.NewGuid();

        Guid UserIdFor(string employeeNumber, string? preferredUserName = null)
        {
            if (preferredUserName is not null && userByUserName.TryGetValue(preferredUserName, out var byName))
                return byName.Id;
            var employeeId = employees[employeeNumber].Id;
            return userByUserName.Values.FirstOrDefault(u => u.EmployeeId == employeeId)?.Id ?? fallbackUserId;
        }

        var managerUserId = userByUserName.GetValueOrDefault("manager")?.Id ?? fallbackUserId;
        var managerName = "Anil Kumar";
        var submittedAt = _clock.UtcNow.AddDays(-1);

        // 1) EMP006 — pending CL matching the contract example (2026-11-03..04, 2 units).
        var priya = employees["EMP006"];
        var pendingCl = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            EmployeeId = priya.Id,
            LeaveTypeId = types["CL"].Id,
            StartDate = new DateOnly(2026, 11, 3),
            EndDate = new DateOnly(2026, 11, 4),
            DayType = LeaveDayType.Full,
            Units = 2m,
            Reason = "Family function in Mysuru.",
            Status = LeaveRequestStatus.Pending,
            SubmittedAt = submittedAt,
            CreatedByUserId = UserIdFor("EMP006", "employee")
        };
        pendingCl.ApprovalTaskId = await AddTaskAsync("Leave", pendingCl.Id,
            $"Casual Leave (2 days) 03 Nov to 04 Nov ({priya.DisplayName})",
            pendingCl.CreatedByUserId, priya.Id, priya.DisplayName, ApprovalStatus.Pending, managerUserId, managerName,
            null, null, cancellationToken);

        // 2) EMP007 — approved CL (2026-10-05..07, 3 units) with full history.
        var rohit = employees["EMP007"];
        var approvedCl = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            EmployeeId = rohit.Id,
            LeaveTypeId = types["CL"].Id,
            StartDate = new DateOnly(2026, 10, 5),
            EndDate = new DateOnly(2026, 10, 7),
            DayType = LeaveDayType.Full,
            Units = 3m,
            Reason = "Home repairs at Coorg.",
            Status = LeaveRequestStatus.Approved,
            SubmittedAt = _clock.UtcNow.AddDays(-16),
            DecidedAt = _clock.UtcNow.AddDays(-15),
            ApproverName = managerName,
            CreatedByUserId = UserIdFor("EMP007", "employee2")
        };
        approvedCl.ApprovalTaskId = await AddTaskAsync("Leave", approvedCl.Id,
            $"Casual Leave (3 days) 05 Oct to 07 Oct ({rohit.DisplayName})",
            approvedCl.CreatedByUserId, rohit.Id, rohit.DisplayName, ApprovalStatus.Approved,
            managerUserId, managerName, ApprovalActionType.Approve, "Enjoy the break.", cancellationToken);

        // 3) EMP010 — pending SL with attachment (2026-10-27..28, 2 units).
        var ananya = employees["EMP010"];
        var pendingSl = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            EmployeeId = ananya.Id,
            LeaveTypeId = types["SL"].Id,
            StartDate = new DateOnly(2026, 10, 27),
            EndDate = new DateOnly(2026, 10, 28),
            DayType = LeaveDayType.Full,
            Units = 2m,
            Reason = "Fever and rest advised by doctor.",
            AttachmentUrl = "files://sl-certificate-emp010.pdf",
            Status = LeaveRequestStatus.Pending,
            SubmittedAt = submittedAt,
            CreatedByUserId = UserIdFor("EMP010")
        };
        pendingSl.ApprovalTaskId = await AddTaskAsync("Leave", pendingSl.Id,
            $"Sick Leave (2 days) 27 Oct to 28 Oct ({ananya.DisplayName})",
            pendingSl.CreatedByUserId, ananya.Id, ananya.DisplayName, ApprovalStatus.Pending, managerUserId, managerName,
            null, null, cancellationToken);

        // 4) EMP009 — rejected EL with a reason (2026-09-14..18, 5 units).
        var vikram = employees["EMP009"];
        var rejectedEl = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            EmployeeId = vikram.Id,
            LeaveTypeId = types["EL"].Id,
            StartDate = new DateOnly(2026, 9, 14),
            EndDate = new DateOnly(2026, 9, 18),
            DayType = LeaveDayType.Full,
            Units = 5m,
            Reason = "Trip to Leh.",
            Status = LeaveRequestStatus.Rejected,
            SubmittedAt = _clock.UtcNow.AddDays(-42),
            DecidedAt = _clock.UtcNow.AddDays(-41),
            RejectionReason = "Client release that week; please re-plan after 30 Sep.",
            CreatedByUserId = UserIdFor("EMP009")
        };
        rejectedEl.ApprovalTaskId = await AddTaskAsync("Leave", rejectedEl.Id,
            $"Earned Leave (5 days) 14 Sep to 18 Sep ({vikram.DisplayName})",
            rejectedEl.CreatedByUserId, vikram.Id, vikram.DisplayName, ApprovalStatus.Rejected,
            userByUserName.GetValueOrDefault("admin")?.Id ?? fallbackUserId, "Ravi Sharma",
            ApprovalActionType.Reject, rejectedEl.RejectionReason, cancellationToken);

        _db.Set<LeaveRequest>().AddRange(pendingCl, approvedCl, pendingSl, rejectedEl);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded {Count} demo leave request(s).", 4);
    }

    // ---------------------------------------------------------------- approval task rows

    private async Task<Guid> AddTaskAsync(string requestType, Guid requestId, string title,
        Guid requesterUserId, Guid? requesterEmployeeId, string requesterName, ApprovalStatus status,
        Guid? approverUserId, string? approverName,
        ApprovalActionType? finalAction, string? finalComment, CancellationToken cancellationToken)
    {
        var workflow = await _db.ApprovalWorkflowDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(w => w.RequestType == requestType && w.IsActive, cancellationToken);

        var createdAt = _clock.UtcNow.AddDays(-1);
        var task = new ApprovalTask
        {
            Id = Guid.NewGuid(),
            RequestType = requestType,
            RequestId = requestId,
            Title = title,
            RequesterUserId = requesterUserId,
            RequesterEmployeeId = requesterEmployeeId,
            RequesterName = requesterName,
            Status = status.ToString(),
            CurrentStepNumber = 1,
            CurrentStepName = "Line manager",
            CurrentAssigneeUserId = status == ApprovalStatus.Pending ? approverUserId : null,
            CurrentAssigneeRole = status == ApprovalStatus.Pending ? "LineManager" : null,
            WorkflowDefinitionId = workflow?.Id,
            WorkflowVersion = workflow?.Version ?? 1,
            CreatedAt = createdAt,
            UpdatedAt = status == ApprovalStatus.Pending ? createdAt : _clock.UtcNow.AddDays(-0.5),
            DueAt = status == ApprovalStatus.Pending ? _clock.UtcNow.AddDays(1) : null,
            CompletedAt = status == ApprovalStatus.Pending ? null : _clock.UtcNow.AddDays(-0.5)
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
            At = createdAt
        });

        if (finalAction is { } action)
        {
            task.Actions.Add(new ApprovalAction
            {
                Id = Guid.NewGuid(),
                TaskId = task.Id,
                StepNumber = 1,
                Action = action,
                ActorUserId = approverUserId,
                ActorName = approverName ?? "Approver",
                Comment = finalComment,
                At = task.UpdatedAt
            });
        }

        _db.Set<ApprovalTask>().Add(task);
        await _db.SaveChangesAsync(cancellationToken);
        return task.Id;
    }
}
