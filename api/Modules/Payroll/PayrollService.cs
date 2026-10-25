using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Auditing;
using Wintech.Hrms.Api.Common.CurrentUser;
using Wintech.Hrms.Api.Common.ErrorHandling;
using Wintech.Hrms.Api.Common.Registration;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Payroll;
using Wintech.Hrms.Api.Domain.Employees;
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
    Task<PayrollImportPreviewResult> PreviewPayslipImportAsync(Guid periodId, PayrollPayslipImportRequest request,
        CancellationToken cancellationToken = default);
    Task<int> ImportPayslipsAsync(Guid periodId, PayrollPayslipImportRequest request,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PayrollAdminPayslipRow>> GetAdminPayslipsAsync(Guid periodId,
        CancellationToken cancellationToken = default);
    Task<PayrollPublishResult> PublishPayslipsAsync(Guid periodId, string reason,
        CancellationToken cancellationToken = default);
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

    public async Task<PayrollImportPreviewResult> PreviewPayslipImportAsync(Guid periodId,
        PayrollPayslipImportRequest request, CancellationToken cancellationToken = default)
    {
        EnsurePayrollManage();
        var period = await _db.Set<PayrollPeriod>().AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == periodId, cancellationToken)
            ?? throw new NotFoundException("Payroll period not found.");
        return await ValidatePayslipImportAsync(period, request, cancellationToken);
    }

    public async Task<int> ImportPayslipsAsync(Guid periodId, PayrollPayslipImportRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsurePayrollManage();
        var period = await _db.Set<PayrollPeriod>()
            .FirstOrDefaultAsync(item => item.Id == periodId, cancellationToken)
            ?? throw new NotFoundException("Payroll period not found.");
        EnsurePeriodOpen(period);
        var preview = await ValidatePayslipImportAsync(period, request, cancellationToken);
        if (preview.ValidRows != preview.TotalRows)
            throw new RequestValidationException("The payroll import contains invalid rows.",
                new() { ["rows"] = preview.Rows.Where(row => !row.Valid)
                    .Select(row => $"Row {row.Row} ({row.EmployeeNumber}): {string.Join("; ", row.Errors)}").ToArray() });

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var employees = await _db.Employees
            .Where(employee => request.Rows.Select(row => row.EmployeeNumber.Trim()).Contains(employee.EmployeeNumber))
            .ToDictionaryAsync(employee => employee.EmployeeNumber, cancellationToken);
        foreach (var row in request.Rows)
        {
            var employee = employees[row.EmployeeNumber.Trim()];
            var payslip = new Payslip
            {
                Id = Guid.NewGuid(),
                PeriodId = period.Id,
                EmployeeId = employee.Id,
                StartDate = period.StartDate,
                EndDate = period.EndDate,
                Gross = row.Gross,
                TotalDeductions = row.TotalDeductions,
                NetPay = row.NetPay,
                Currency = row.Currency.Trim().ToUpperInvariant(),
                Status = PayslipStatus.Draft,
                GeneratedAt = _clock.UtcNow,
                Components = row.Components.Select(component => new PayslipComponent
                {
                    Id = Guid.NewGuid(),
                    Component = component.Component.Trim(),
                    Amount = component.Amount,
                    Kind = component.Kind!.Value
                }).ToList()
            };
            _db.Add(payslip);
        }
        _audit.Add("payroll.payslips.import", nameof(PayrollPeriod), period.Id.ToString(), details: new
        {
            period.Label,
            importedCount = request.Rows.Count,
            employeeNumbers = request.Rows.Select(row => row.EmployeeNumber.Trim()).ToArray()
        });
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return request.Rows.Count;
    }

    public async Task<IReadOnlyList<PayrollAdminPayslipRow>> GetAdminPayslipsAsync(Guid periodId,
        CancellationToken cancellationToken = default)
    {
        EnsurePayrollManage();
        if (!await _db.Set<PayrollPeriod>().AnyAsync(period => period.Id == periodId, cancellationToken))
            throw new NotFoundException("Payroll period not found.");
        return await _db.Set<Payslip>().AsNoTracking()
            .Where(payslip => payslip.PeriodId == periodId)
            .OrderBy(payslip => payslip.Employee!.EmployeeNumber)
            .Select(payslip => new PayrollAdminPayslipRow(
                payslip.Id,
                payslip.Employee!.EmployeeNumber,
                payslip.Employee.DisplayName,
                payslip.Gross,
                payslip.TotalDeductions,
                payslip.NetPay,
                payslip.Currency,
                payslip.Status.ToString(),
                payslip.GeneratedAt,
                payslip.PublishedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<PayrollPublishResult> PublishPayslipsAsync(Guid periodId, string reason,
        CancellationToken cancellationToken = default)
    {
        _currentUser.EnsureActive();
        if (!_currentUser.HasPermission(Permissions.PayrollPublish))
            throw new ForbiddenException("You do not have permission to publish payslips.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new RequestValidationException("A publication reason is required.",
                new() { ["reason"] = ["Provide a reason for publishing this payroll output."] });
        var period = await _db.Set<PayrollPeriod>().FirstOrDefaultAsync(item => item.Id == periodId, cancellationToken)
            ?? throw new NotFoundException("Payroll period not found.");
        EnsurePeriodOpen(period);
        var drafts = await _db.Set<Payslip>()
            .Where(payslip => payslip.PeriodId == periodId && payslip.Status == PayslipStatus.Draft)
            .ToListAsync(cancellationToken);
        if (drafts.Count == 0)
            throw new ConflictException("There are no draft payslips to publish.");
        var publishedAt = _clock.UtcNow;
        foreach (var payslip in drafts)
        {
            payslip.Status = PayslipStatus.Published;
            payslip.PublishedAt = publishedAt;
        }
        _audit.Add("payroll.payslips.publish", nameof(PayrollPeriod), period.Id.ToString(), details: new
        {
            period.Label,
            count = drafts.Count,
            reason = reason.Trim()
        });
        await _db.SaveChangesAsync(cancellationToken);
        return new PayrollPublishResult(period.Id, drafts.Count, publishedAt);
    }

    private async Task<PayrollImportPreviewResult> ValidatePayslipImportAsync(PayrollPeriod period,
        PayrollPayslipImportRequest request, CancellationToken cancellationToken)
    {
        EnsurePeriodOpen(period);
        if (request.Rows.Count is < 1 or > 1000)
            throw new RequestValidationException("A payroll import must contain between 1 and 1000 rows.",
                new() { ["rows"] = ["Choose a payroll file with 1–1000 rows."] });
        var numbers = request.Rows.Select(row => row.EmployeeNumber.Trim()).Where(number => number.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var employees = await _db.Employees.AsNoTracking()
            .Where(employee => numbers.Contains(employee.EmployeeNumber))
            .Select(employee => new { employee.Id, employee.EmployeeNumber, employee.EmploymentStatus })
            .ToListAsync(cancellationToken);
        var employeeByNumber = employees.ToDictionary(employee => employee.EmployeeNumber,
            StringComparer.OrdinalIgnoreCase);
        var existing = await _db.Set<Payslip>().AsNoTracking()
            .Where(payslip => payslip.PeriodId == period.Id)
            .Select(payslip => payslip.Employee!.EmployeeNumber)
            .ToListAsync(cancellationToken);
        var existingNumbers = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<PayrollImportRowResult>(request.Rows.Count);
        for (var index = 0; index < request.Rows.Count; index++)
        {
            var row = request.Rows[index];
            var number = row.EmployeeNumber.Trim();
            var errors = new List<string>();
            if (number.Length == 0) errors.Add("Employee number is required.");
            if (!employeeByNumber.TryGetValue(number, out var employee))
                errors.Add("Employee number was not found.");
            else if (employee.EmploymentStatus is not (EmploymentStatus.Active or EmploymentStatus.Probation))
                errors.Add("Payroll output can only be imported for active employees.");
            if (!seen.Add(number)) errors.Add("Employee appears more than once in this import.");
            if (existingNumbers.Contains(number)) errors.Add("A payslip already exists for this employee and period.");
            if (row.Gross < 0 || row.TotalDeductions < 0 || row.NetPay < 0)
                errors.Add("Gross, deductions and net pay must be non-negative.");
            if (Math.Round(row.Gross - row.TotalDeductions, 2) != Math.Round(row.NetPay, 2))
                errors.Add("Net pay must equal gross less deductions.");
            if (row.Currency.Trim().Length != 3)
                errors.Add("Currency must be a three-letter ISO code.");
            if (row.Components.Count > 100 || row.Components.Any(component =>
                    string.IsNullOrWhiteSpace(component.Component) || component.Amount < 0 || component.Kind is null))
                errors.Add("Payslip components must have a name, non-negative amount and earning/deduction kind.");
            results.Add(new PayrollImportRowResult(index + 1, number, errors.Count == 0, errors));
        }
        return new PayrollImportPreviewResult(period.Id, period.Label, results.Count,
            results.Count(result => result.Valid), results);
    }

    private void EnsurePayrollManage()
    {
        _currentUser.EnsureActive();
        if (!_currentUser.HasPermission(Permissions.PayrollManage))
            throw new ForbiddenException("You do not have permission to manage payroll inputs.");
    }

    private static void EnsurePeriodOpen(PayrollPeriod period)
    {
        if (period.Status != PayrollPeriodStatus.Open)
            throw new ConflictException("Payroll input can only be changed while the period is open.");
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
