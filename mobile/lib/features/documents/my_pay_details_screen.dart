import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/utils/formatters.dart';
import '../../providers/admin_payroll_provider.dart';
import '../../providers/api_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';

/// `GET /me/salary` — the signed-in employee's own pay details and computed tax.
/// Read-only: only ever the caller's own record (the API never accepts an id here).
final mySalaryProvider = FutureProvider<AdminSalaryStructure>((ref) async {
  final json = await ref.watch(apiClientProvider).getMap('/me/salary');
  return AdminSalaryStructure.fromJson(json);
});

/// Employee self-service view of pay details: CTC breakup, employer contributions
/// and the income tax the system computes automatically from those figures.
class MyPayDetailsScreen extends ConsumerWidget {
  const MyPayDetailsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final salary = ref.watch(mySalaryProvider);
    return AppScaffold(
      title: 'My pay details',
      body: AsyncBody(
        value: salary,
        onRetry: () => ref.invalidate(mySalaryProvider),
        padding: const EdgeInsets.all(16),
        builder: (row) {
          final earnings = <_Line>[
            if (row.basicAnnual > 0) _Line('Basic', row.basicAnnual),
            if (row.hraAnnual > 0) _Line('HRA', row.hraAnnual),
            if (row.daAnnual > 0) _Line('DA', row.daAnnual),
            if (row.specialAllowanceAnnual > 0)
              _Line('Special allowance', row.specialAllowanceAnnual),
            if (row.otherAllowanceAnnual > 0)
              _Line('Other allowance', row.otherAllowanceAnnual),
          ];
          final employer = <_Line>[
            if (row.employerPfAnnual > 0) _Line('Employer PF', row.employerPfAnnual),
            if (row.gratuityAnnual > 0) _Line('Gratuity', row.gratuityAnnual),
          ];
          final tax = row.tax;
          return RefreshIndicator(
            onRefresh: () async => ref.invalidate(mySalaryProvider),
            child: ListView(
              physics: const AlwaysScrollableScrollPhysics(),
              children: [
                // Headline: CTC and monthly gross.
                Card(
                  child: Padding(
                    padding: const EdgeInsets.all(16),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          'Annual CTC',
                          style: Theme.of(context).textTheme.labelLarge,
                        ),
                        const SizedBox(height: 4),
                        Text(
                          AppFormatters.currency(row.annualCtc),
                          style: Theme.of(context).textTheme.headlineSmall,
                        ),
                        const SizedBox(height: 4),
                        Text(
                          'Monthly gross ${AppFormatters.currency(row.monthlyGross)}'
                          ' · ${row.taxRegime} tax regime',
                          style: Theme.of(context).textTheme.bodySmall,
                        ),
                      ],
                    ),
                  ),
                ),
                const SizedBox(height: 12),
                _BreakupCard(
                  title: 'Earnings (annual)',
                  lines: earnings,
                  annualTotal: row.annualCtc -
                      row.employerPfAnnual -
                      row.gratuityAnnual,
                ),
                if (employer.isNotEmpty) ...[
                  const SizedBox(height: 12),
                  _BreakupCard(
                    title: 'Employer contributions (part of CTC)',
                    lines: employer,
                    annualTotal:
                        row.employerPfAnnual + row.gratuityAnnual,
                  ),
                ],
                const SizedBox(height: 12),
                Card(
                  child: Padding(
                    padding: const EdgeInsets.all(16),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          'Income tax (computed automatically)',
                          style: Theme.of(context).textTheme.titleSmall,
                        ),
                        const SizedBox(height: 8),
                        _row(
                          context,
                          'Financial year',
                          'FY ${tax.financialYear}-${(tax.financialYear + 1) % 100}',
                        ),
                        _row(
                          context,
                          'Annual gross',
                          AppFormatters.currency(tax.annualGross),
                        ),
                        _row(
                          context,
                          'Standard deduction',
                          AppFormatters.currency(tax.standardDeduction),
                        ),
                        if (row.deduction80CAnnual > 0 ||
                            row.deduction80DAnnual > 0 ||
                            row.homeLoanInterestAnnual > 0 ||
                            row.otherDeductionsAnnual > 0)
                          _row(
                            context,
                            'Chapter VI-A deductions',
                            AppFormatters.currency(
                              row.deduction80CAnnual +
                                  row.deduction80DAnnual +
                                  row.homeLoanInterestAnnual +
                                  row.otherDeductionsAnnual,
                            ),
                          ),
                        _row(
                          context,
                          'Taxable income',
                          AppFormatters.currency(tax.taxableIncome),
                        ),
                        _row(
                          context,
                          'Tax before rebate',
                          AppFormatters.currency(tax.taxBeforeRebate),
                        ),
                        if (tax.rebate > 0)
                          _row(
                            context,
                            'Rebate u/s 87A',
                            '− ${AppFormatters.currency(tax.rebate)}',
                          ),
                        _row(
                          context,
                          'Health & education cess',
                          AppFormatters.currency(tax.cess),
                        ),
                        const Divider(),
                        _row(
                          context,
                          'Annual tax',
                          AppFormatters.currency(tax.annualTax),
                        ),
                        _row(
                          context,
                          'Monthly TDS',
                          AppFormatters.currency(tax.monthlyTds),
                          emphasise: true,
                        ),
                        const SizedBox(height: 8),
                        Text(
                          'Professional tax '
                          '${AppFormatters.currency(row.professionalTaxMonthly)} per month. '
                          'Tax is recomputed whenever finance updates pay details.',
                          style: Theme.of(context).textTheme.bodySmall,
                        ),
                      ],
                    ),
                  ),
                ),
              ],
            ),
          );
        },
      ),
    );
  }

  Widget _row(
    BuildContext context,
    String label,
    String value, {
    bool emphasise = false,
  }) =>
      Padding(
        padding: const EdgeInsets.symmetric(vertical: 2),
        child: Row(
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          children: [
            Flexible(child: Text(label)),
            Text(
              value,
              style: emphasise
                  ? Theme.of(context).textTheme.titleMedium
                  : Theme.of(context).textTheme.bodyMedium,
            ),
          ],
        ),
      );
}

class _Line {
  const _Line(this.label, this.annual);
  final String label;
  final num annual;
}

/// Small annual/monthly table for one pay breakup section.
class _BreakupCard extends StatelessWidget {
  const _BreakupCard({
    required this.title,
    required this.lines,
    required this.annualTotal,
  });

  final String title;
  final List<_Line> lines;
  final num annualTotal;

  @override
  Widget build(BuildContext context) {
    final text = Theme.of(context).textTheme;
    return Card(
      child: Padding(
        padding: const EdgeInsets.fromLTRB(16, 16, 16, 8),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(title, style: text.titleSmall),
            const SizedBox(height: 8),
            Row(
              children: [
                Expanded(child: Text('Component', style: text.labelSmall)),
                SizedBox(
                  width: 96,
                  child: Text(
                    'Per year',
                    textAlign: TextAlign.right,
                    style: text.labelSmall,
                  ),
                ),
                SizedBox(
                  width: 96,
                  child: Text(
                    'Per month',
                    textAlign: TextAlign.right,
                    style: text.labelSmall,
                  ),
                ),
              ],
            ),
            const Divider(),
            for (final line in lines)
              Padding(
                padding: const EdgeInsets.symmetric(vertical: 3),
                child: Row(
                  children: [
                    Expanded(child: Text(line.label)),
                    SizedBox(
                      width: 96,
                      child: Text(
                        AppFormatters.currencyCompact(line.annual),
                        textAlign: TextAlign.right,
                      ),
                    ),
                    SizedBox(
                      width: 96,
                      child: Text(
                        AppFormatters.currencyCompact(line.annual / 12),
                        textAlign: TextAlign.right,
                      ),
                    ),
                  ],
                ),
              ),
            const Divider(),
            Padding(
              padding: const EdgeInsets.symmetric(vertical: 4),
              child: Row(
                children: [
                  Expanded(
                    child: Text('Total', style: text.titleSmall),
                  ),
                  SizedBox(
                    width: 96,
                    child: Text(
                      AppFormatters.currencyCompact(annualTotal),
                      textAlign: TextAlign.right,
                      style: text.titleSmall,
                    ),
                  ),
                  SizedBox(
                    width: 96,
                    child: Text(
                      AppFormatters.currencyCompact(annualTotal / 12),
                      textAlign: TextAlign.right,
                      style: text.titleSmall,
                    ),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}
