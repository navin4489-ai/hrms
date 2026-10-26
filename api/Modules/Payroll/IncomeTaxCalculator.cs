using Wintech.Hrms.Api.Domain.Payroll;

namespace Wintech.Hrms.Api.Modules.Payroll;

/// <summary>One slab boundary: income up to <see cref="UpperBound"/> (INR, annual) taxed at <see cref="Rate"/>.</summary>
public sealed record TaxSlab(decimal? UpperBound, decimal Rate);

/// <summary>Result of the annual income-tax computation shown to finance and embedded in payslips.</summary>
public sealed record IncomeTaxEstimate(
    string Regime,
    int FinancialYear,
    decimal AnnualGross,
    decimal StandardDeduction,
    decimal ChapterViADeductions,
    decimal TaxableIncome,
    decimal TaxBeforeRebate,
    decimal Rebate87A,
    decimal Cess,
    decimal AnnualTax,
    decimal MonthlyTds,
    IReadOnlyList<TaxSlabLine> Slabs);

public sealed record TaxSlabLine(decimal From, decimal? To, decimal Rate, decimal Tax);

/// <summary>
/// Automatic Indian income-tax (TDS) estimation from annual pay details.
/// Slabs follow the Income-tax Act for the applicable financial year (April–March);
/// the calculator never stores tax — it is recomputed from the pay record every time.
/// </summary>
public static class IncomeTaxCalculator
{
    /// <summary>New-regime slabs for FY 2024-25 (Budget 2024).</summary>
    private static readonly TaxSlab[] NewRegime2024 =
    [
        new(300_000m, 0m), new(700_000m, 0.05m), new(1_000_000m, 0.10m),
        new(1_200_000m, 0.15m), new(1_500_000m, 0.20m), new(null, 0.30m)
    ];

    /// <summary>New-regime slabs for FY 2025-26 onward (Budget 2025); also used for later years.</summary>
    private static readonly TaxSlab[] NewRegime2025 =
    [
        new(400_000m, 0m), new(800_000m, 0.05m), new(1_200_000m, 0.10m),
        new(1_600_000m, 0.15m), new(2_000_000m, 0.20m), new(2_400_000m, 0.25m),
        new(null, 0.30m)
    ];

    private static readonly TaxSlab[] OldRegime =
    [
        new(250_000m, 0m), new(500_000m, 0.05m), new(1_000_000m, 0.20m), new(null, 0.30m)
    ];

    public static int FinancialYearOf(DateOnly date) => date.Month >= 4 ? date.Year : date.Year - 1;

    /// <summary>Standard deduction under the applicable regime for the financial year.</summary>
    public static decimal StandardDeduction(TaxRegime regime, int fy) =>
        regime == TaxRegime.Old ? 50_000m : fy >= 2025 ? 75_000m : 50_000m;

    /// <summary>Compute the annual income tax for one employee's annual gross pay.</summary>
    /// <param name="regime">New (concessional) or Old (with Chapter VI-A deductions).</param>
    /// <param name="fy">Indian financial year the income belongs to (e.g. 2026 for FY 2026-27).</param>
    /// <param name="annualGross">Total annual salary income (basic + allowances).</param>
    /// <param name="chapterViA">Employee-declared deductions: 80C + 80D + home-loan interest + other (old regime only).</param>
    public static IncomeTaxEstimate Compute(TaxRegime regime, int fy, decimal annualGross, decimal chapterViA = 0m)
    {
        if (annualGross < 0) annualGross = 0;
        if (chapterViA < 0) chapterViA = 0;

        var standard = StandardDeduction(regime, fy);
        // The standard deduction may not exceed total income; Chapter VI-A is capped
        // per section by the caller, but never allowed to push taxable income below zero.
        var appliedStandard = Math.Min(standard, annualGross);
        var appliedViA = regime == TaxRegime.Old ? Math.Min(chapterViA, annualGross - appliedStandard) : 0m;
        if (appliedViA < 0) appliedViA = 0;
        var taxable = annualGross - appliedStandard - appliedViA;
        if (taxable < 0) taxable = 0;

        var slabs = regime == TaxRegime.Old ? OldRegime : fy >= 2025 ? NewRegime2025 : NewRegime2024;
        var lines = new List<TaxSlabLine>(slabs.Length);
        var lower = 0m;
        var beforeRebate = 0m;
        foreach (var slab in slabs)
        {
            if (taxable <= lower) break;
            var ceiling = slab.UpperBound ?? taxable;
            var bandTop = Math.Min(ceiling, taxable);
            var tax = Math.Round((bandTop - lower) * slab.Rate, 2);
            lines.Add(new TaxSlabLine(lower, slab.UpperBound, slab.Rate, tax));
            beforeRebate += tax;
            if (slab.UpperBound is null) break;
            lower = slab.UpperBound.Value;
        }
        beforeRebate = Math.Round(beforeRebate, 2);

        // Section 87A rebate: new regime FY 2025+ up to ₹12L taxable (₹60k), FY 2024 up to
        // ₹7L (₹25k); old regime up to ₹5L (₹12.5k). Rebate cannot exceed the tax itself.
        var (rebateLimit, rebateCap) = regime == TaxRegime.Old
            ? (500_000m, 12_500m)
            : fy >= 2025 ? (1_200_000m, 60_000m) : (700_000m, 25_000m);
        var rebate = taxable <= rebateLimit ? Math.Min(beforeRebate, rebateCap) : 0m;

        var afterRebate = Math.Max(0m, beforeRebate - rebate);
        var cess = Math.Round(afterRebate * 0.04m, 2);
        var total = Math.Round(afterRebate + cess, 2);

        return new IncomeTaxEstimate(
            regime == TaxRegime.Old ? "Old" : "New",
            fy,
            Math.Round(annualGross, 2),
            appliedStandard,
            appliedViA,
            Math.Round(taxable, 2),
            beforeRebate,
            Math.Round(rebate, 2),
            cess,
            total,
            Math.Round(total / 12m, 2),
            lines);
    }
}
