using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Auditing;
using Wintech.Hrms.Api.Common.Notifications;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Approvals;
using Wintech.Hrms.Api.Domain.Attendance;

namespace Wintech.Hrms.Api.Modules.Attendance;

/// <summary>
/// Applies an attendance correction to the daily record exactly once when the workflow
/// finishes (FR-ADM-042/044, UAT-005: originals are preserved on the correction row).
/// </summary>
public sealed class AttendanceCorrectionApprovalHandler : IApprovalHandler
{
    private readonly AppDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly INotificationPublisher _notifications;

    public AttendanceCorrectionApprovalHandler(AppDbContext db, IClock clock, IAuditService audit,
        INotificationPublisher notifications)
    {
        _db = db;
        _clock = clock;
        _audit = audit;
        _notifications = notifications;
    }

    public string RequestType => "AttendanceCorrection";

    public async Task OnCompletedAsync(ApprovalTask task, ApprovalActionType finalAction, string? comment,
        CancellationToken cancellationToken = default)
    {
        var correction = await _db.Set<AttendanceCorrectionRequest>()
            .FirstOrDefaultAsync(c => c.Id == task.RequestId, cancellationToken);
        if (correction is null || correction.Status != AttendanceCorrectionStatus.Pending) return; // exactly once

        correction.Status = finalAction switch
        {
            ApprovalActionType.Approve => AttendanceCorrectionStatus.Approved,
            ApprovalActionType.Reject => AttendanceCorrectionStatus.Rejected,
            _ => AttendanceCorrectionStatus.Returned
        };
        correction.DecidedAt = _clock.UtcNow;
        correction.DecisionComment = comment;

        if (correction.Status == AttendanceCorrectionStatus.Approved)
            await ApplyToRecordAsync(correction, cancellationToken);

        _audit.Add($"Attendance.Correction.{correction.Status}", nameof(AttendanceCorrectionRequest),
            correction.Id.ToString(), details: new
            {
                workDate = correction.WorkDate,
                status = correction.Status.ToString(),
                comment,
                originalCheckIn = correction.OriginalCheckIn,
                originalCheckOut = correction.OriginalCheckOut,
                correctedCheckIn = correction.ProposedCheckIn,
                correctedCheckOut = correction.ProposedCheckOut
            });

        await _notifications.PublishAsync(new NotificationMessage(
            task.RequesterUserId, $"AttendanceCorrection{correction.Status}",
            $"Attendance correction {correction.Status.ToString().ToLowerInvariant()}",
            $"Your {correction.Type} correction for {correction.WorkDate:yyyy-MM-dd} was {correction.Status.ToString().ToLowerInvariant()}." +
            (string.IsNullOrWhiteSpace(comment) ? string.Empty : $" Comment: {comment}"),
            $"/attendance/corrections/{correction.Id}", "Attendance", EmployeeId: correction.EmployeeId),
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task OnCancelledAsync(ApprovalTask task, string? reason, CancellationToken cancellationToken = default)
    {
        var correction = await _db.Set<AttendanceCorrectionRequest>()
            .FirstOrDefaultAsync(c => c.Id == task.RequestId, cancellationToken);
        if (correction is null || correction.Status != AttendanceCorrectionStatus.Pending) return;

        correction.Status = AttendanceCorrectionStatus.Cancelled;
        correction.DecidedAt = _clock.UtcNow;
        correction.DecisionComment = reason;

        _audit.Add("Attendance.Correction.Cancelled", nameof(AttendanceCorrectionRequest), correction.Id.ToString(),
            details: new { workDate = correction.WorkDate, reason });

        await _notifications.PublishAsync(new NotificationMessage(
            task.RequesterUserId, "AttendanceCorrectionCancelled",
            "Attendance correction cancelled",
            $"Your {correction.Type} correction for {correction.WorkDate:yyyy-MM-dd} was cancelled.",
            $"/attendance/corrections/{correction.Id}", "Attendance", EmployeeId: correction.EmployeeId),
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Writes the approved times onto the daily record, creating it when absent (FR-ADM-044).</summary>
    private async Task ApplyToRecordAsync(AttendanceCorrectionRequest correction, CancellationToken cancellationToken)
    {
        var record = await _db.Set<AttendanceDayRecord>()
            .FirstOrDefaultAsync(r => r.EmployeeId == correction.EmployeeId && r.WorkDate == correction.WorkDate,
                cancellationToken);

        if (record is null)
        {
            var assignment = await _db.Set<ShiftAssignment>().AsNoTracking()
                .FirstOrDefaultAsync(a => a.EmployeeId == correction.EmployeeId && a.Date == correction.WorkDate,
                    cancellationToken);
            record = new AttendanceDayRecord
            {
                Id = Guid.NewGuid(),
                EmployeeId = correction.EmployeeId,
                WorkDate = correction.WorkDate,
                ShiftId = assignment?.ShiftId,
                Status = AttendanceDayStatus.NotMarked,
                Source = AttendanceSource.Admin,
                UpdatedAt = _clock.UtcNow
            };
            _db.Set<AttendanceDayRecord>().Add(record);
        }

        record.CheckIn = correction.ProposedCheckIn ?? record.CheckIn ?? correction.OriginalCheckIn;
        record.CheckOut = correction.ProposedCheckOut ?? record.CheckOut ?? correction.OriginalCheckOut;
        record.Source = AttendanceSource.Admin;
        record.UpdatedAt = _clock.UtcNow;

        var shift = record.ShiftId is { } shiftId
            ? await _db.Set<WorkShift>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == shiftId, cancellationToken)
            : null;

        record.LateByMinutes = record.CheckIn is { } checkIn && shift is not null
            ? Math.Max(0, checkIn.Hour * 60 + checkIn.Minute
                - (shift.StartTime.Hour * 60 + shift.StartTime.Minute + shift.GraceMinutes))
            : 0;
        record.WorkedHours = AttendanceTimeHelper.WorkedHours(record.CheckIn, record.CheckOut, shift?.BreakMinutes ?? 0);
        record.OvertimeHours = record.WorkedHours is { } worked && worked > 8 ? Math.Round(worked - 8, 2) : 0;
        record.IsHalfDay = record.WorkedHours is { } hours && hours > 0 && hours < 4;
        record.Status = record.CheckIn is null
            ? AttendanceDayStatus.NotMarked
            : record.CheckOut is null
                ? (record.LateByMinutes > 0 ? AttendanceDayStatus.Late : AttendanceDayStatus.Present)
                : record.IsHalfDay
                    ? AttendanceDayStatus.HalfDay
                    : record.LateByMinutes > 0 ? AttendanceDayStatus.Late : AttendanceDayStatus.Present;
    }
}

/// <summary>
/// Applies an approved shift swap to the roster exactly once and keeps both employees informed
/// (FR-EMP-051). On rejection/return/cancellation only the request status changes.
/// </summary>
public sealed class ShiftSwapApprovalHandler : IApprovalHandler
{
    private readonly AppDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly INotificationPublisher _notifications;

    public ShiftSwapApprovalHandler(AppDbContext db, IClock clock, IAuditService audit,
        INotificationPublisher notifications)
    {
        _db = db;
        _clock = clock;
        _audit = audit;
        _notifications = notifications;
    }

    public string RequestType => "ShiftSwap";

    public async Task OnCompletedAsync(ApprovalTask task, ApprovalActionType finalAction, string? comment,
        CancellationToken cancellationToken = default)
    {
        var swap = await _db.Set<ShiftSwapRequest>()
            .FirstOrDefaultAsync(s => s.Id == task.RequestId, cancellationToken);
        if (swap is null || swap.Status != ShiftSwapStatus.Pending) return; // exactly once

        swap.Status = finalAction switch
        {
            ApprovalActionType.Approve => ShiftSwapStatus.Approved,
            ApprovalActionType.Reject => ShiftSwapStatus.Rejected,
            _ => ShiftSwapStatus.Returned
        };
        swap.DecidedAt = _clock.UtcNow;
        swap.DecisionComment = comment;

        if (swap.Status == ShiftSwapStatus.Approved && !swap.Applied)
            await ApplySwapAsync(swap, cancellationToken);

        _audit.Add($"ShiftSwap.{swap.Status}", nameof(ShiftSwapRequest), swap.Id.ToString(), details: new
        {
            rosterDate = swap.RosterDate,
            swap.ProposedWithEmployeeId,
            swap.ProposedShiftId,
            status = swap.Status.ToString(),
            comment
        });

        await NotifyAsync(swap, task.RequesterUserId, comment, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task OnCancelledAsync(ApprovalTask task, string? reason, CancellationToken cancellationToken = default)
    {
        var swap = await _db.Set<ShiftSwapRequest>()
            .FirstOrDefaultAsync(s => s.Id == task.RequestId, cancellationToken);
        if (swap is null || swap.Status != ShiftSwapStatus.Pending) return;

        swap.Status = ShiftSwapStatus.Cancelled;
        swap.DecidedAt = _clock.UtcNow;
        swap.DecisionComment = reason;

        _audit.Add("ShiftSwap.Cancelled", nameof(ShiftSwapRequest), swap.Id.ToString(),
            details: new { rosterDate = swap.RosterDate, reason });

        await NotifyAsync(swap, task.RequesterUserId, reason, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Exchange the two rostered shifts, or move the requester onto the proposed shift.</summary>
    private async Task ApplySwapAsync(ShiftSwapRequest swap, CancellationToken cancellationToken)
    {
        var mine = await _db.Set<ShiftAssignment>()
            .FirstOrDefaultAsync(a => a.EmployeeId == swap.EmployeeId && a.Date == swap.RosterDate, cancellationToken);
        var theirs = await _db.Set<ShiftAssignment>()
            .FirstOrDefaultAsync(a => a.EmployeeId == swap.ProposedWithEmployeeId && a.Date == swap.RosterDate,
                cancellationToken);

        if (mine is not null)
        {
            if (swap.ProposedShiftId is { } targetShiftId)
            {
                var previousShiftId = mine.ShiftId;
                mine.ShiftId = targetShiftId;
                if (theirs is not null && theirs.ShiftId == targetShiftId) theirs.ShiftId = previousShiftId;
            }
            else if (theirs is not null)
            {
                (mine.ShiftId, theirs.ShiftId) = (theirs.ShiftId, mine.ShiftId);
            }

            mine.Note = $"Shift swap approved on {_clock.UtcNow:yyyy-MM-dd}";
        }

        swap.Applied = true;
    }

    private async Task NotifyAsync(ShiftSwapRequest swap, Guid requesterUserId, string? comment,
        CancellationToken cancellationToken)
    {
        var statusText = swap.Status.ToString().ToLowerInvariant();
        await _notifications.PublishAsync(new NotificationMessage(
            requesterUserId, $"ShiftSwap{swap.Status}",
            $"Shift swap {statusText}",
            $"Your shift swap request for {swap.RosterDate:yyyy-MM-dd} was {statusText}." +
            (string.IsNullOrWhiteSpace(comment) ? string.Empty : $" Comment: {comment}"),
            $"/shifts/swap-requests/{swap.Id}", "Attendance", EmployeeId: swap.EmployeeId),
            cancellationToken);

        if (swap.Status == ShiftSwapStatus.Approved)
        {
            var colleagueUserId = await _db.UserAccounts.AsNoTracking()
                .Where(u => u.EmployeeId == swap.ProposedWithEmployeeId)
                .OrderBy(u => u.Id)
                .Select(u => (Guid?)u.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (colleagueUserId is not null)
            {
                await _notifications.PublishAsync(new NotificationMessage(
                    colleagueUserId, "ShiftSwapApplied",
                    "Shift swap applied",
                    $"Your rostered shift on {swap.RosterDate:yyyy-MM-dd} was changed as part of an approved swap.",
                    $"/shifts/roster", "Attendance", EmployeeId: swap.ProposedWithEmployeeId),
                    cancellationToken);
            }
        }
    }
}
