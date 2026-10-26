using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Auditing;
using Wintech.Hrms.Api.Common.CurrentUser;
using Wintech.Hrms.Api.Common.ErrorHandling;
using Wintech.Hrms.Api.Common.Registration;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Common.Tenancy;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Payroll;
using Wintech.Hrms.Api.Domain.Employees;
using Wintech.Hrms.Api.Security;
using Wintech.Hrms.Api.Modules.Organizations;

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
    Task<PayrollPeriodDto> CreatePeriodAsync(CreatePayrollPeriodRequest request, CancellationToken cancellationToken = default);
    Task<PayrollPeriodDto> SetPeriodStateAsync(Guid id, PayrollPeriodStatus target, string reason, CancellationToken cancellationToken = default);
    Task<PayrollImportPreviewResult> PreviewPayslipImportAsync(Guid periodId, PayrollPayslipImportRequest request,
        CancellationToken cancellationToken = default);
    Task<int> ImportPayslipsAsync(Guid periodId, PayrollPayslipImportRequest request,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PayrollAdminPayslipRow>> GetAdminPayslipsAsync(Guid periodId,
        CancellationToken cancellationToken = default);
    Task<PayrollPublishResult> PublishPayslipsAsync(Guid periodId, string reason,
        CancellationToken cancellationToken = default);

    // Salary structures & automatic tax (finance fills pay details; tax is computed from them).
    Task<IReadOnlyList<AdminSalaryStructureDto>> GetSalaryStructuresAsync(string? search,
        CancellationToken cancellationToken = default);
    Task<AdminSalaryStructureDto> SaveSalaryStructureAsync(Guid employeeId, SaveSalaryStructureRequest request,
        CancellationToken cancellationToken = default);
    Task<IncomeTaxEstimateDto> PreviewTaxAsync(SaveSalaryStructureRequest request,
        CancellationToken cancellationToken = default);
    Task<PayrollGenerateResult> GeneratePayslipsAsync(Guid periodId, CancellationToken cancellationToken = default);
}

public sealed class PayrollService : IPayrollService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly IClock _clock;
    private readonly IOrganizationService _organizations;

    public PayrollService(AppDbContext db, ICurrentUser currentUser, IAuditService audit, IClock clock,
        IOrganizationService organizations)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
        _organizations = organizations;
    }

    public async Task<PayrollImportPreviewResult> PreviewPayslipImportAsync(Guid periodId,
        PayrollPayslipImportRequest request, CancellationToken cancellationToken = default)
    {
        await EnsurePayrollModuleAsync(cancellationToken);
        EnsurePayrollManage();
        var period = await _db.Set<PayrollPeriod>().AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == periodId, cancellationToken)
            ?? throw new NotFoundException("Payroll period not found.");
        return await ValidatePayslipImportAsync(period, request, cancellationToken);
    }

    public async Task<int> ImportPayslipsAsync(Guid periodId, PayrollPayslipImportRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsurePayrollModuleAsync(cancellationToken);
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
        await EnsurePayrollModuleAsync(cancellationToken);
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
        await EnsurePayrollModuleAsync(cancellationToken);
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

    // ------------------------------------------------------------------ salary structures & automatic tax

    public async Task<IReadOnlyList<AdminSalaryStructureDto>> GetSalaryStructuresAsync(string? search,
        CancellationToken cancellationToken = default)
    {
        await EnsurePayrollModuleAsync(cancellationToken);
        EnsurePayrollManage();
        var term = string.IsNullOrWhiteSpace(search) ? null : search.Trim();

        var employees = _db.Employees.AsNoTracking()
            .Where(e => e.EmploymentStatus == EmploymentStatus.Active || e.EmploymentStatus == EmploymentStatus.Probation);
        if (term is not null)
            employees = employees.Where(e =>
                e.EmployeeNumber.Contains(term) || e.DisplayName.Contains(term));

        var employeeRows = await employees
            .OrderBy(e => e.EmployeeNumber)
            .Select(e => new
            {
                e.Id, e.EmployeeNumber, e.DisplayName,
                Department = e.Department != null ? e.Department.Name : null
            })
            .ToListAsync(cancellationToken);
        var structures = await _db.Set<EmployeeSalaryStructure>().AsNoTracking()
            .ToDictionaryAsync(s => s.EmployeeId, cancellationToken);

        var fy = IncomeTaxCalculator.FinancialYearOf(_clock.TodayIn("Asia/Kolkata"));
        return employeeRows
            .Select(row => MapStructure(row.Id, row.EmployeeNumber, row.DisplayName, row.Department,
                structures.GetValueOrDefault(row.Id), fy))
            .ToList();
    }

    public async Task<AdminSalaryStructureDto> SaveSalaryStructureAsync(Guid employeeId,
        SaveSalaryStructureRequest request, CancellationToken cancellationToken = default)
    {
        await EnsurePayrollModuleAsync(cancellationToken);
        EnsurePayrollManage();
        var employee = await _db.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId)
            .Select(e => new { e.Id, e.EmployeeNumber, e.DisplayName, Department = e.Department != null ? e.Department.Name : null })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Employee not found.");

        var gross = GrossAnnualOf(request);
        if (gross <= 0)
            throw new RequestValidationException("Annual earnings must be greater than zero.",
                new() { ["basicAnnual"] = ["Enter the employee's annual pay details (basic and allowances)."] });

        var structure = await _db.Set<EmployeeSalaryStructure>()
            .FirstOrDefaultAsync(s => s.EmployeeId == employeeId, cancellationToken);
        if (structure is null)
        {
            structure = new EmployeeSalaryStructure { Id = Guid.NewGuid(), EmployeeId = employeeId };
            _db.Add(structure);
        }

        structure.BasicAnnual = request.BasicAnnual;
        structure.HraAnnual = request.HraAnnual;
        structure.DaAnnual = request.DaAnnual;
        structure.SpecialAllowanceAnnual = request.SpecialAllowanceAnnual;
        structure.OtherAllowanceAnnual = request.OtherAllowanceAnnual;
        structure.EmployerPfAnnual = request.EmployerPfAnnual;
        structure.GratuityAnnual = request.GratuityAnnual;
        structure.ProfessionalTaxMonthly = request.ProfessionalTaxMonthly;
        structure.TaxRegime = ParseRegime(request.TaxRegime);
        structure.Deduction80CAnnual = request.Deduction80CAnnual;
        structure.Deduction80DAnnual = request.Deduction80DAnnual;
        structure.HomeLoanInterestAnnual = request.HomeLoanInterestAnnual;
        structure.OtherDeductionsAnnual = request.OtherDeductionsAnnual;
        structure.UpdatedAt = _clock.UtcNow;
        structure.UpdatedByUserId = _currentUser.UserId;

        // Audited: pay details are compensation data and every change is traceable.
        _audit.Add("payroll.salary.update", nameof(EmployeeSalaryStructure), structure.Id.ToString(), details: new
        {
            employee.EmployeeNumber,
            structure.BasicAnnual, structure.HraAnnual, structure.DaAnnual,
            structure.SpecialAllowanceAnnual, structure.OtherAllowanceAnnual,
            structure.TaxRegime, AnnualCtc = gross
        });
        await _db.SaveChangesAsync(cancellationToken);

        var fy = IncomeTaxCalculator.FinancialYearOf(_clock.TodayIn("Asia/Kolkata"));
        return MapStructure(employee.Id, employee.EmployeeNumber, employee.DisplayName, employee.Department, structure, fy);
    }

    public Task<IncomeTaxEstimateDto> PreviewTaxAsync(SaveSalaryStructureRequest request,
        CancellationToken cancellationToken = default)
    {
        // Preview is read-only and permission-checked like any other payroll view; it works
        // from unsaved values so finance can see the tax before committing the pay details.
        _currentUser.EnsureActive();
        if (!_currentUser.HasPermission(Permissions.PayrollManage))
            throw new ForbiddenException("You do not have permission to manage payroll inputs.");
        var fy = IncomeTaxCalculator.FinancialYearOf(_clock.TodayIn("Asia/Kolkata"));
        var estimate = ComputeTax(request, fy);
        return Task.FromResult(MapTax(estimate));
    }

    public async Task<PayrollGenerateResult> GeneratePayslipsAsync(Guid periodId,
        CancellationToken cancellationToken = default)
    {
        await EnsurePayrollModuleAsync(cancellationToken);
        EnsurePayrollManage();
        var period = await _db.Set<PayrollPeriod>()
            .FirstOrDefaultAsync(p => p.Id == periodId, cancellationToken)
            ?? throw new NotFoundException("Payroll period not found.");
        EnsurePeriodOpen(period);

        // A full calendar month pays a full month; partial or multi-month windows prorate by
        // the share of each calendar month the period covers.
        var monthFactor = PeriodMonthFactor(period.StartDate, period.EndDate);

        var employees = await _db.Employees.AsNoTracking()
            .Where(e => e.EmploymentStatus == EmploymentStatus.Active || e.EmploymentStatus == EmploymentStatus.Probation)
            .Select(e => new { e.Id, e.EmployeeNumber, e.DisplayName })
            .ToListAsync(cancellationToken);
        var structures = await _db.Set<EmployeeSalaryStructure>().AsNoTracking()
            .ToDictionaryAsync(s => s.EmployeeId, cancellationToken);

        var existing = await _db.Set<Payslip>().AsNoTracking()
            .Where(p => p.PeriodId == period.Id)
            .Select(p => p.EmployeeId)
            .ToHashSetAsync(cancellationToken);

        var generated = 0;
        var skipped = new List<string>();
        foreach (var employee in employees)
        {
            if (!structures.TryGetValue(employee.Id, out var structure) || existing.Contains(employee.Id))
            {
                skipped.Add(employee.EmployeeNumber);
                continue;
            }

            var payslip = BuildPayslip(period, employee.Id, structure, monthFactor);
            _db.Add(payslip);
            generated++;
        }

        if (generated == 0)
            throw new ConflictException(
                "No payslips were generated. Every active employee either already has a payslip for this period or has no pay details recorded.");

        _audit.Add("payroll.payslips.generate", nameof(PayrollPeriod), period.Id.ToString(), details: new
        {
            period.Label,
            generated,
            skipped = skipped.Count
        });
        await _db.SaveChangesAsync(cancellationToken);
        return new PayrollGenerateResult(period.Id, period.Label, generated, skipped.Count, skipped);
    }

    /// <summary>Builds one draft payslip from an employee's pay details for the given period.</summary>
    internal Payslip BuildPayslip(PayrollPeriod period, Guid employeeId, EmployeeSalaryStructure structure,
        decimal monthFactor)
    {
        var monthlyBasic = structure.BasicAnnual / 12m;
        var earnings = new List<(string Name, decimal Amount)>
        {
            ("Basic", monthlyBasic)
        };
        if (structure.HraAnnual > 0) earnings.Add(("HRA", structure.HraAnnual / 12m));
        if (structure.DaAnnual > 0) earnings.Add(("DA", structure.DaAnnual / 12m));
        if (structure.SpecialAllowanceAnnual > 0) earnings.Add(("Special Allowance", structure.SpecialAllowanceAnnual / 12m));
        if (structure.OtherAllowanceAnnual > 0) earnings.Add(("Other Allowance", structure.OtherAllowanceAnnual / 12m));

        var monthlyGross = earnings.Sum(e => e.Amount);

        // Statutory employee deductions: PF 12% of basic subject to the ₹15,000 wage ceiling,
        // ESI 0.75% when monthly gross is at or below ₹21,000, plus state professional tax.
        var employeePf = Math.Round(Math.Min(monthlyBasic, 15_000m) * 0.12m, 2);
        var esi = monthlyGross <= 21_000m ? Math.Round(monthlyGross * 0.0075m, 2) : 0m;
        var ptax = structure.ProfessionalTaxMonthly;
        var fy = IncomeTaxCalculator.FinancialYearOf(period.StartDate);
        var monthlyTds = ComputeTax(structure, fy).MonthlyTds;

        var deductions = new List<(string Name, decimal Amount)>();
        if (employeePf > 0) deductions.Add(("Employee PF", employeePf));
        if (esi > 0) deductions.Add(("Employee ESI", esi));
        if (ptax > 0) deductions.Add(("Professional Tax", ptax));
        if (monthlyTds > 0) deductions.Add(("Income Tax (TDS)", monthlyTds));

        var gross = Math.Round(monthlyGross * monthFactor, 2);
        var totalDeductions = Math.Round(deductions.Sum(d => d.Amount) * monthFactor, 2);

        var components = earnings
            .Select(e => new PayslipComponent
            {
                Id = Guid.NewGuid(),
                Component = e.Name,
                Amount = Math.Round(e.Amount * monthFactor, 2),
                Kind = PayslipComponentKind.Earning
            })
            .Concat(deductions.Select(d => new PayslipComponent
            {
                Id = Guid.NewGuid(),
                Component = d.Name,
                Amount = Math.Round(d.Amount * monthFactor, 2),
                Kind = PayslipComponentKind.Deduction
            }))
            .ToList();

        return new Payslip
        {
            Id = Guid.NewGuid(),
            PeriodId = period.Id,
            EmployeeId = employeeId,
            StartDate = period.StartDate,
            EndDate = period.EndDate,
            Gross = gross,
            TotalDeductions = totalDeductions,
            NetPay = Math.Round(gross - totalDeductions, 2),
            Currency = structure.Currency.Trim().ToUpperInvariant(),
            Status = PayslipStatus.Draft,
            GeneratedAt = _clock.UtcNow,
            Components = components
        };
    }

    private static decimal GrossAnnualOf(SaveSalaryStructureRequest request) =>
        request.BasicAnnual + request.HraAnnual + request.DaAnnual +
        request.SpecialAllowanceAnnual + request.OtherAllowanceAnnual;

    private static decimal GrossAnnualOf(EmployeeSalaryStructure structure) =>
        structure.BasicAnnual + structure.HraAnnual + structure.DaAnnual +
        structure.SpecialAllowanceAnnual + structure.OtherAllowanceAnnual;

    private static TaxRegime ParseRegime(string? value) =>
        string.Equals(value?.Trim(), "Old", StringComparison.OrdinalIgnoreCase) ? TaxRegime.Old : TaxRegime.New;

    private static IncomeTaxEstimate ComputeTax(SaveSalaryStructureRequest request, int fy) =>
        IncomeTaxCalculator.Compute(ParseRegime(request.TaxRegime), fy, GrossAnnualOf(request),
            request.Deduction80CAnnual + request.Deduction80DAnnual + request.HomeLoanInterestAnnual +
            request.OtherDeductionsAnnual);

    private static IncomeTaxEstimate ComputeTax(EmployeeSalaryStructure structure, int fy) =>
        IncomeTaxCalculator.Compute(structure.TaxRegime, fy, GrossAnnualOf(structure),
            structure.Deduction80CAnnual + structure.Deduction80DAnnual + structure.HomeLoanInterestAnnual +
            structure.OtherDeductionsAnnual);

    /// <summary>Share of calendar months covered by the period: 1.0 for a full month.</summary>
    public static decimal PeriodMonthFactor(DateOnly start, DateOnly end)
    {
        decimal factor = 0;
        var cursor = new DateOnly(start.Year, start.Month, 1);
        while (cursor <= end)
        {
            var monthEnd = new DateOnly(cursor.Year, cursor.Month, DateTime.DaysInMonth(cursor.Year, cursor.Month));
            var overlapStart = start > cursor ? start : cursor;
            var overlapEnd = end < monthEnd ? end : monthEnd;
            if (overlapStart <= overlapEnd)
                factor += (overlapEnd.DayNumber - overlapStart.DayNumber + 1) /
                          (decimal)DateTime.DaysInMonth(cursor.Year, cursor.Month);
            cursor = cursor.AddMonths(1);
        }
        return factor;
    }

    private static IncomeTaxEstimateDto MapTax(IncomeTaxEstimate estimate) => new(
        estimate.Regime, estimate.FinancialYear, estimate.AnnualGross, estimate.StandardDeduction,
        estimate.ChapterViADeductions, estimate.TaxableIncome, estimate.TaxBeforeRebate,
        estimate.Rebate87A, estimate.Cess, estimate.AnnualTax, estimate.MonthlyTds);

    private static AdminSalaryStructureDto MapStructure(Guid employeeId, string employeeNumber, string employeeName,
        string? department, EmployeeSalaryStructure? structure, int fy)
    {
        var regime = structure?.TaxRegime ?? TaxRegime.New;
        var basic = structure?.BasicAnnual ?? 0m;
        var gross = structure is null ? 0m : GrossAnnualOf(structure);
        var estimate = IncomeTaxCalculator.Compute(regime, fy, gross,
            structure?.Deduction80CAnnual + structure?.Deduction80DAnnual +
            structure?.HomeLoanInterestAnnual + structure?.OtherDeductionsAnnual ?? 0m);
        return new AdminSalaryStructureDto(
            employeeId, employeeNumber, employeeName, department,
            basic,
            structure?.HraAnnual ?? 0m,
            structure?.DaAnnual ?? 0m,
            structure?.SpecialAllowanceAnnual ?? 0m,
            structure?.OtherAllowanceAnnual ?? 0m,
            structure?.EmployerPfAnnual ?? 0m,
            structure?.GratuityAnnual ?? 0m,
            structure?.ProfessionalTaxMonthly ?? 0m,
            regime == TaxRegime.Old ? "Old" : "New",
            structure?.Deduction80CAnnual ?? 0m,
            structure?.Deduction80DAnnual ?? 0m,
            structure?.HomeLoanInterestAnnual ?? 0m,
            structure?.OtherDeductionsAnnual ?? 0m,
            Math.Round(gross + (structure?.EmployerPfAnnual ?? 0m) + (structure?.GratuityAnnual ?? 0m), 2),
            Math.Round(gross / 12m, 2),
            MapTax(estimate),
            structure?.UpdatedAt);
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

    private async Task EnsurePayrollModuleAsync(CancellationToken cancellationToken)
    {
        var enabled = (await _organizations.GetMyModulesAsync(cancellationToken))
            .Any(module => string.Equals(module.Key, OrgModules.Payroll, StringComparison.OrdinalIgnoreCase)
                           && module.Enabled);
        if (!enabled)
            throw new ForbiddenException("Payroll is not enabled for your organization.");
    }

    private static void EnsurePeriodOpen(PayrollPeriod period)
    {
        if (period.Status != PayrollPeriodStatus.Open)
            throw new ConflictException("Payroll input can only be changed while the period is open.");
    }

    public async Task<IReadOnlyList<PayslipListItem>> GetPayslipsAsync(int? year, Guid? employeeId, CancellationToken cancellationToken = default)
    {
        await EnsurePayrollModuleAsync(cancellationToken);
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
        await EnsurePayrollModuleAsync(cancellationToken);
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
        await EnsurePayrollModuleAsync(cancellationToken);
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
        await EnsurePayrollModuleAsync(cancellationToken);
        EnsurePayrollManage();
        var periods = await _db.Set<PayrollPeriod>().AsNoTracking()
            .OrderByDescending(p => p.StartDate)
            .ToListAsync(cancellationToken);
        return periods.Select(Map).ToList();
    }

    public async Task<PayrollPeriodDto> CreatePeriodAsync(CreatePayrollPeriodRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsurePayrollModuleAsync(cancellationToken);
        EnsurePayrollManage();

        if (request.StartDate == default || request.EndDate == default || request.CutOffDate == default)
            throw new RequestValidationException(errors: new()
            {
                ["dates"] = ["Start date, end date and cut-off date are required."]
            });
        if (request.EndDate < request.StartDate)
            throw new RequestValidationException(errors: new()
            {
                ["endDate"] = ["End date must be on or after the start date."]
            });
        if (request.EndDate.DayNumber - request.StartDate.DayNumber >= 366)
            throw new RequestValidationException(errors: new()
            {
                ["endDate"] = ["A payroll period cannot be longer than 366 days."]
            });
        if (request.CutOffDate < request.EndDate)
            throw new RequestValidationException(errors: new()
            {
                ["cutOffDate"] = ["Cut-off date cannot be before the period ends."]
            });

        var label = string.IsNullOrWhiteSpace(request.Label)
            ? request.StartDate.ToString("MMM yyyy", System.Globalization.CultureInfo.InvariantCulture)
            : request.Label.Trim();
        if (label.Length > 50)
            throw new RequestValidationException(errors: new()
            {
                ["label"] = ["Period label cannot exceed 50 characters."]
            });

        var overlaps = await _db.Set<PayrollPeriod>().AsNoTracking().AnyAsync(period =>
            period.StartDate <= request.EndDate && period.EndDate >= request.StartDate,
            cancellationToken);
        if (overlaps)
            throw new ConflictException("This date range overlaps an existing payroll period.");

        var labelExists = await _db.Set<PayrollPeriod>().AsNoTracking()
            .AnyAsync(period => period.Label.ToLower() == label.ToLower(), cancellationToken);
        if (labelExists)
            throw new ConflictException("A payroll period with this label already exists.");

        var periodEntity = new PayrollPeriod
        {
            Id = Guid.NewGuid(),
            Label = label,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            CutOffDate = request.CutOffDate,
            Status = PayrollPeriodStatus.Open,
            CreatedAt = _clock.UtcNow
        };
        _db.Set<PayrollPeriod>().Add(periodEntity);
        _audit.Add("payroll.period.create", nameof(PayrollPeriod), periodEntity.Id.ToString(), details: new
        {
            periodEntity.Label,
            periodEntity.StartDate,
            periodEntity.EndDate,
            periodEntity.CutOffDate
        });
        await _db.SaveChangesAsync(cancellationToken);
        return Map(periodEntity);
    }

    public async Task<PayrollPeriodDto> SetPeriodStateAsync(Guid id, PayrollPeriodStatus target, string reason, CancellationToken cancellationToken = default)
    {
        await EnsurePayrollModuleAsync(cancellationToken);
        EnsurePayrollManage();
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
