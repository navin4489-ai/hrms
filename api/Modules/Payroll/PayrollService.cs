using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Auditing;
using Wintech.Hrms.Api.Common.CurrentUser;
using Wintech.Hrms.Api.Common.ErrorHandling;
using Wintech.Hrms.Api.Common.Registration;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Payroll;
using Wintech.Hrms.Api.Security;

namespace Wintech.Hrms.Api.Modules.Payroll;

/// <summary>
/// Payroll & payslips use cases (contract §10, FR-EMP-060..064, FR-ADM-070..076).
/// Payroll calculation is out of scope (BRD R-02): periods and imported/generated payslips
/// are read, scoped and published here.
/// </summary>
public interface IPayrollService : IAppService
{
    Task<IReadOnlyList<PayslipListItem>> GetPayslipsAsync(int? year, Guid? employeeId, CancellationToken cancellationToken = default);
    Task<PayslipDetailDto> GetPayslipAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PdfDownload> GeneratePayslipPdfAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PayrollPeriodDto>> GetPeriodsAsync(CancellationToken cancellationToken = default);
    Task<PayrollPeriodDto> SetPeriodStateAsync(Guid id, PayrollPeriodStatus target, string reason, CancellationToken cancellationToken = default);
}

public sealed class PayrollService : IPayrollService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly IClock _clock;

    public PayrollService(AppDbContext db, ICurrentUser currentUser, IAuditService audit, IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
    }

    public async Task<IReadOnlyList<PayslipListItem>> GetPayslipsAsync(int? year, Guid? employeeId, CancellationToken cancellationToken = default)
    {
        _currentUser.EnsureActive();
        var targetEmployeeId = ResolvePayslipScope(employeeId);

        var query = _db.Set<Payslip>().AsNoTracking()
            .Where(p => p.EmployeeId == targetEmployeeId && p.Status == PayslipStatus.Published);

        if (year is > 1900 and < 3000)
        {
            var from = new DateOnly(year.Value, 1, 1);
            var to = from.AddYears(1);
            query = query.Where(p => p.Period!.StartDate >= from && p.Period.StartDate < to);
        }

        var rows = await query
            .OrderByDescending(p => p.Period!.StartDate)
            .Select(p => new
            {
                p.Id,
                p.PeriodId,
                p.Period!.StartDate,
                p.EndDate,
                p.Gross,
                p.NetPay,
                p.Currency,
                p.Status,
                p.PublishedAt
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new PayslipListItem(
            r.Id, r.PeriodId, FormatPeriodLabel(r.StartDate), r.StartDate, r.EndDate,
            r.Gross, r.NetPay, r.Currency, r.Status, r.PublishedAt)).ToList();
    }

    public async Task<PayslipDetailDto> GetPayslipAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _currentUser.EnsureActive();
        var payslip = await LoadScopedPayslipAsync(id, cancellationToken);

        var employee = await _db.Employees.AsNoTracking()
            .Where(e => e.Id == payslip.EmployeeId)
            .Select(e => new
            {
                e.EmployeeNumber,
                e.DisplayName,
                LegalEntity = e.Organization != null ? e.Organization.Name : ""
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("The requested payslip was not found.");

        var components = await _db.Set<PayslipComponent>().AsNoTracking()
            .Where(c => c.PayslipId == payslip.Id)
            .Select(c => new { c.Component, c.Amount, c.Kind })
            .ToListAsync(cancellationToken);

        // YTD gross: sum of this employee's published payslips in the same year up to this period.
        var periodStart = payslip.Period!.StartDate;
        var yearFrom = new DateOnly(periodStart.Year, 1, 1);
        var yearTo = yearFrom.AddYears(1);
        var ytdRows = await _db.Set<Payslip>().AsNoTracking()
            .Where(p => p.EmployeeId == payslip.EmployeeId &&
                        p.Status == PayslipStatus.Published &&
                        p.Period!.StartDate >= yearFrom && p.Period.StartDate < yearTo &&
                        p.Period.StartDate <= periodStart)
            .Select(p => p.Gross)
            .ToListAsync(cancellationToken);

        var earnings = components.Where(c => c.Kind == PayslipComponentKind.Earning)
            .Select(c => new PayslipComponentDto(c.Component, c.Amount)).ToList();
        var deductions = components.Where(c => c.Kind == PayslipComponentKind.Deduction)
            .Select(c => new PayslipComponentDto(c.Component, c.Amount)).ToList();

        return new PayslipDetailDto(
            payslip.Id,
            FormatPeriodLabel(periodStart),
            payslip.StartDate,
            payslip.EndDate,
            payslip.Currency,
            earnings,
            deductions,
            payslip.Gross,
            payslip.TotalDeductions,
            payslip.NetPay,
            ytdRows.Sum(),
            employee.EmployeeNumber,
            employee.DisplayName,
            employee.LegalEntity,
            payslip.GeneratedAt);
    }

    public async Task<PdfDownload> GeneratePayslipPdfAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _currentUser.EnsureActive();
        var payslip = await LoadScopedPayslipAsync(id, cancellationToken);

        var employee = await _db.Employees.AsNoTracking()
            .Where(e => e.Id == payslip.EmployeeId)
            .Select(e => new
            {
                e.EmployeeNumber,
                e.DisplayName,
                Department = e.Department != null ? e.Department.Name : "",
                Designation = e.Designation != null ? e.Designation.Name : "",
                LegalEntity = e.Organization != null ? e.Organization.Name : ""
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("The requested payslip was not found.");

        var components = await _db.Set<PayslipComponent>().AsNoTracking()
            .Where(c => c.PayslipId == payslip.Id)
            .Select(c => new { c.Component, c.Amount, c.Kind })
            .ToListAsync(cancellationToken);

        var model = new PayslipPdfModel(
            employee.LegalEntity,
            employee.EmployeeNumber,
            employee.DisplayName,
            employee.Department,
            employee.Designation,
            FormatPeriodLabel(payslip.Period!.StartDate),
            payslip.StartDate,
            payslip.EndDate,
            components.Where(c => c.Kind == PayslipComponentKind.Earning)
                .Select(c => new PayslipComponentDto(c.Component, c.Amount)).ToList(),
            components.Where(c => c.Kind == PayslipComponentKind.Deduction)
                .Select(c => new PayslipComponentDto(c.Component, c.Amount)).ToList(),
            payslip.Gross,
            payslip.TotalDeductions,
            payslip.NetPay,
            payslip.Currency,
            _clock.UtcNow);

        var bytes = PayslipPdf.Generate(model);

        // FR-EMP-062 / FR-ADM-152: every payslip download is an audited, authenticated read.
        await _audit.RecordAsync("Payslip.Download", "Payslip", payslip.Id.ToString(),
            details: new
            {
                payslip.PeriodId,
                PeriodLabel = FormatPeriodLabel(payslip.Period.StartDate),
                employee.EmployeeNumber,
                Scope = _currentUser.EmployeeId == payslip.EmployeeId ? "Own" : "Privileged"
            }, cancellationToken: cancellationToken);

        return new PdfDownload(bytes, $"Payslip-{FormatPeriodLabel(payslip.Period.StartDate).Replace(' ', '-')}-{employee.EmployeeNumber}.pdf");
    }

    public async Task<IReadOnlyList<PayrollPeriodDto>> GetPeriodsAsync(CancellationToken cancellationToken = default)
    {
        var periods = await _db.Set<PayrollPeriod>().AsNoTracking()
            .OrderByDescending(p => p.StartDate)
            .ToListAsync(cancellationToken);
        return periods.Select(Map).ToList();
    }

    public async Task<PayrollPeriodDto> SetPeriodStateAsync(Guid id, PayrollPeriodStatus target, string reason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new RequestValidationException(errors: new() { ["reason"] = ["A reason is required."] });

        var period = await _db.Set<PayrollPeriod>()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException("The payroll period was not found.");

        if (target == PayrollPeriodStatus.Locked)
        {
            if (period.Status == PayrollPeriodStatus.Locked)
                throw new ConflictException("The payroll period is already locked.");
            if (period.Status == PayrollPeriodStatus.Closed)
                throw new ConflictException("The payroll period is closed and cannot be locked again.");

            period.Status = PayrollPeriodStatus.Locked;
            period.LockedAt = _clock.UtcNow;
            period.LockedByUserId = _currentUser.UserId;
            period.LockReason = reason;

            // FR-ADM-075: locking a period after approval is an audited state change.
            _audit.Add("PayrollPeriod.Lock", "PayrollPeriod", period.Id.ToString(),
                details: new { period.Label, reason });
        }
        else
        {
            if (period.Status == PayrollPeriodStatus.Open)
                throw new ConflictException("The payroll period is not locked.");
            if (period.Status == PayrollPeriodStatus.Closed)
                throw new ConflictException("A closed payroll period cannot be reopened.");

            period.Status = PayrollPeriodStatus.Open;
            period.LockedAt = null;
            period.LockedByUserId = null;
            period.LockReason = reason;

            // FR-ADM-075: reopening a locked period requires a reason and is audited.
            _audit.Add("PayrollPeriod.Unlock", "PayrollPeriod", period.Id.ToString(),
                details: new { period.Label, reason });
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Map(period);
    }

    /// <summary>
    /// Payslip list is always the caller's own; another employee's payslip is only reachable
    /// with payslip.view.any and never via a guessed id (UAT-006 -> 404, no existence leak).
    /// </summary>
    private Guid ResolvePayslipScope(Guid? requestedEmployeeId)
    {
        var own = _currentUser.EmployeeId
                  ?? throw new ForbiddenException("No employee record is linked to your account.");

        if (requestedEmployeeId is null || requestedEmployeeId == own) return own;

        if (_currentUser.HasPermission(Permissions.PayslipViewAny)) return requestedEmployeeId.Value;

        throw new NotFoundException("The requested payslip was not found.");
    }

    /// <summary>Loads a payslip; another employee's payslip is 404 unless payslip.view.any is held (UAT-006).</summary>
    private async Task<Payslip> LoadScopedPayslipAsync(Guid id, CancellationToken cancellationToken)
    {
        var payslip = await _db.Set<Payslip>().AsNoTracking()
            .Include(p => p.Period)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (payslip is null) throw new NotFoundException("The requested payslip was not found.");

        var own = _currentUser.EmployeeId;
        if (payslip.EmployeeId != own && !_currentUser.HasPermission(Permissions.PayslipViewAny))
            throw new NotFoundException("The requested payslip was not found.");

        if (payslip.Status != PayslipStatus.Published)
            throw new ConflictException("The payslip has not been published yet.");

        return payslip;
    }

    private static PayrollPeriodDto Map(PayrollPeriod period) =>
        new(period.Id, period.Label, period.StartDate, period.EndDate, period.Status,
            period.CutOffDate, period.LockedAt);

    internal static string FormatPeriodLabel(DateOnly start) => start.ToString("MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
}
