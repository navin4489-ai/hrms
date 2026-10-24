using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Registration;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Payroll;

namespace Wintech.Hrms.Api.Data.Seed;

/// <summary>
/// Idempotent payroll seed (Order 120): 2026 payroll periods (Closed/Locked behind us, the
/// current month Open) and published payslips with realistic earnings/deductions for the demo
/// employees for the most recent closed/locked months. Payroll calculation itself is out of
/// scope (BRD R-02) — inputs here represent imported payroll output (FR-ADM-074).
/// </summary>
public sealed class PayrollSeedData : IDataSeeder
{
    private static readonly (string EmployeeNumber, decimal Basic, decimal SpecialAllowance, decimal IncomeTax)[] SalaryStructure =
    [
        ("EMP001", 120000m, 30000m, 22000m),
        ("EMP002", 110000m, 25000m, 18500m),
        ("EMP003", 105000m, 25000m, 17500m),
        ("EMP004", 55000m, 12000m, 6500m),
        ("EMP005", 60000m, 12000m, 7200m),
        ("EMP006", 65000m, 15000m, 8000m),
        ("EMP007", 60000m, 12000m, 7000m),
        ("EMP008", 62000m, 13000m, 7500m)
    ];

    private const decimal ConveyanceAllowance = 2400m;
    private const decimal ProfessionalTax = 200m;
    private const decimal AugustFestivalBonus = 5000m;

    private readonly AppDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<PayrollSeedData> _logger;

    public PayrollSeedData(AppDbContext db, IClock clock, ILogger<PayrollSeedData> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public int Order => 120;

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var periods = await SeedPeriodsAsync(cancellationToken);
        await SeedPayslipsAsync(periods, cancellationToken);
    }

    private async Task<Dictionary<int, PayrollPeriod>> SeedPeriodsAsync(CancellationToken cancellationToken)
    {
        var existing = await _db.Set<PayrollPeriod>().AsNoTracking().ToListAsync(cancellationToken);
        if (existing.Count > 0)
            return existing.ToDictionary(p => p.StartDate.Month);

        var today = _clock.Today;
        var previousMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var periods = new List<PayrollPeriod>();

        for (var month = 1; month <= 12; month++)
        {
            var start = new DateOnly(2026, month, 1);
            var end = start.AddMonths(1).AddDays(-1);
            var status = end < today
                ? (start == previousMonth ? PayrollPeriodStatus.Locked : PayrollPeriodStatus.Closed)
                : PayrollPeriodStatus.Open;

            periods.Add(new PayrollPeriod
            {
                Id = Guid.NewGuid(),
                Label = start.ToString("MMM yyyy", CultureInfo.InvariantCulture),
                StartDate = start,
                EndDate = end,
                Status = status,
                CutOffDate = end.AddDays(5), // attendance/leave input cutoff, 5th of the next month (FR-ADM-071)
                LockedAt = status == PayrollPeriodStatus.Open
                    ? null
                    : DateTime.SpecifyKind(end.ToDateTime(TimeOnly.MinValue).AddDays(5), DateTimeKind.Utc),
                LockReason = status == PayrollPeriodStatus.Open ? null : "Routine monthly payroll close.",
                CreatedAt = DateTime.UtcNow
            });
        }

        _db.Set<PayrollPeriod>().AddRange(periods);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded {Count} payroll periods for 2026.", periods.Count);
        return periods.ToDictionary(p => p.StartDate.Month);
    }

    private async Task SeedPayslipsAsync(Dictionary<int, PayrollPeriod> periods, CancellationToken cancellationToken)
    {
        if (await _db.Set<Payslip>().AsNoTracking().AnyAsync(cancellationToken)) return;

        var employeeIds = await _db.Employees.AsNoTracking()
            .Where(e => SalaryStructure.Select(s => s.EmployeeNumber).Contains(e.EmployeeNumber))
            .Select(e => new { e.Id, e.EmployeeNumber })
            .ToListAsync(cancellationToken);

        // Published payslips for the three most recent finished months (Jul-Sep 2026).
        foreach (var month in new[] { 7, 8, 9 })
        {
            if (!periods.TryGetValue(month, out var period) || period.Status == PayrollPeriodStatus.Open)
                continue;

            var publishedAt = DateTime.SpecifyKind(
                period.EndDate.ToDateTime(TimeOnly.MinValue).AddDays(6).AddHours(10), DateTimeKind.Utc);

            foreach (var employee in employeeIds)
            {
                var salary = SalaryStructure.First(s => s.EmployeeNumber == employee.EmployeeNumber);
                var hra = Math.Round(salary.Basic * 0.40m, 0);
                var providentFund = Math.Round(salary.Basic * 0.12m, 0);
                var bonus = month == 8 ? AugustFestivalBonus : 0m;

                var earnings = new List<(string Component, decimal Amount)>
                {
                    ("Basic", salary.Basic),
                    ("House Rent Allowance", hra),
                    ("Conveyance Allowance", ConveyanceAllowance),
                    ("Special Allowance", salary.SpecialAllowance)
                };
                if (bonus > 0) earnings.Add(("Festival Bonus", bonus));

                var deductions = new List<(string Component, decimal Amount)>
                {
                    ("Provident Fund", providentFund),
                    ("Professional Tax", ProfessionalTax),
                    ("Income Tax (TDS)", salary.IncomeTax)
                };

                var gross = earnings.Sum(e => e.Amount);
                var totalDeductions = deductions.Sum(d => d.Amount);

                var payslip = new Payslip
                {
                    Id = Guid.NewGuid(),
                    PeriodId = period.Id,
                    EmployeeId = employee.Id,
                    StartDate = period.StartDate,
                    EndDate = period.EndDate,
                    Gross = gross,
                    TotalDeductions = totalDeductions,
                    NetPay = gross - totalDeductions,
                    Currency = "INR",
                    Status = PayslipStatus.Published,
                    PublishedAt = publishedAt,
                    GeneratedAt = publishedAt
                };

                foreach (var (component, amount) in earnings)
                    payslip.Components.Add(new PayslipComponent
                    {
                        Id = Guid.NewGuid(),
                        Component = component,
                        Amount = amount,
                        Kind = PayslipComponentKind.Earning
                    });

                foreach (var (component, amount) in deductions)
                    payslip.Components.Add(new PayslipComponent
                    {
                        Id = Guid.NewGuid(),
                        Component = component,
                        Amount = amount,
                        Kind = PayslipComponentKind.Deduction
                    });

                _db.Set<Payslip>().Add(payslip);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        var count = await _db.Set<Payslip>().CountAsync(cancellationToken);
        _logger.LogInformation("Seeded {Count} published payslips for Jul-Sep 2026.", count);
    }
}
