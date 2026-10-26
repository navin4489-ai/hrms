import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/utils/formatters.dart';
import '../../providers/admin_payroll_provider.dart';
import '../../widgets/app_button.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';

/// Finance/admin screen for maintaining employee pay details. Income tax is never
/// typed in — it is recomputed from the pay details by the server and shown live.
class AdminSalaryStructuresScreen extends ConsumerStatefulWidget {
  const AdminSalaryStructuresScreen({super.key});

  @override
  ConsumerState<AdminSalaryStructuresScreen> createState() =>
      _AdminSalaryStructuresScreenState();
}

class _AdminSalaryStructuresScreenState
    extends ConsumerState<AdminSalaryStructuresScreen> {
  String _query = '';

  @override
  Widget build(BuildContext context) {
    final structures = ref.watch(adminSalaryStructuresProvider);
    return AppScaffold(
      title: 'Salary & tax',
      body: Column(
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 4),
            child: TextField(
              onChanged: (value) => setState(() => _query = value.trim()),
              decoration: const InputDecoration(
                hintText: 'Search employee number or name',
                prefixIcon: Icon(Icons.search),
              ),
            ),
          ),
          Expanded(
            child: AsyncBody(
              value: structures,
              onRetry: () =>
                  ref.invalidate(adminSalaryStructuresProvider),
              isEmpty: (data) => data.isEmpty,
              emptyTitle: 'No employees',
              emptyMessage:
                  'Active employees appear here so finance can record their pay details.',
              emptyIcon: Icons.payments_outlined,
              builder: (data) {
                final term = _query.toLowerCase();
                final rows = term.isEmpty
                    ? data
                    : data
                          .where(
                            (row) =>
                                row.employeeNumber.toLowerCase().contains(
                                  term,
                                ) ||
                                row.employeeName.toLowerCase().contains(term),
                          )
                          .toList(growable: false);
                if (rows.isEmpty) {
                  return const Center(
                    child: Padding(
                      padding: EdgeInsets.all(24),
                      child: Text('No employees match your search.'),
                    ),
                  );
                }
                return RefreshIndicator(
                  onRefresh: () async =>
                      ref.invalidate(adminSalaryStructuresProvider),
                  child: ListView.separated(
                    physics: const AlwaysScrollableScrollPhysics(),
                    padding: const EdgeInsets.all(16),
                    itemCount: rows.length,
                    separatorBuilder: (_, _) => const SizedBox(height: 8),
                    itemBuilder: (context, index) {
                      final row = rows[index];
                      return Card(
                        child: ListTile(
                          leading: CircleAvatar(
                            child: Text(
                              row.employeeName.isNotEmpty
                                  ? row.employeeName[0].toUpperCase()
                                  : '?',
                            ),
                          ),
                          title: Text(
                            '${row.employeeName} · ${row.employeeNumber}',
                          ),
                          subtitle: Text(
                            row.hasPayDetails
                                ? 'CTC ${AppFormatters.currencyCompact(row.annualCtc)} · '
                                      'Monthly TDS ${AppFormatters.currencyCompact(row.tax.monthlyTds)} · '
                                      '${row.taxRegime} regime'
                                : 'No pay details recorded yet',
                          ),
                          trailing: const Icon(Icons.chevron_right),
                          onTap: () => _openEditor(row),
                        ),
                      );
                    },
                  ),
                );
              },
            ),
          ),
        ],
      ),
    );
  }

  Future<void> _openEditor(AdminSalaryStructure row) async {
    final saved = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      useSafeArea: true,
      builder: (_) => _SalaryEditor(initial: row),
    );
    if (saved == true) ref.invalidate(adminSalaryStructuresProvider);
  }
}

/// Bottom-sheet form for one employee's pay details with a live tax preview.
class _SalaryEditor extends ConsumerStatefulWidget {
  const _SalaryEditor({required this.initial});

  final AdminSalaryStructure initial;

  @override
  ConsumerState<_SalaryEditor> createState() => _SalaryEditorState();
}

class _SalaryEditorState extends ConsumerState<_SalaryEditor> {
  late final TextEditingController _basic;
  late final TextEditingController _hra;
  late final TextEditingController _da;
  late final TextEditingController _special;
  late final TextEditingController _other;
  late final TextEditingController _employerPf;
  late final TextEditingController _gratuity;
  late final TextEditingController _ptax;
  late final TextEditingController _c80;
  late final TextEditingController _d80;
  late final TextEditingController _homeLoan;
  late final TextEditingController _otherDeductions;
  late String _regime;

  PayrollTaxEstimate? _preview;
  bool _busy = false;
  bool _previewBusy = false;
  Timer? _debounce;

  static String _num(num value) => value == value.roundToDouble()
      ? value.round().toString()
      : value.toStringAsFixed(2);

  @override
  void initState() {
    super.initState();
    final row = widget.initial;
    _basic = TextEditingController(text: _num(row.basicAnnual));
    _hra = TextEditingController(text: _num(row.hraAnnual));
    _da = TextEditingController(text: _num(row.daAnnual));
    _special = TextEditingController(text: _num(row.specialAllowanceAnnual));
    _other = TextEditingController(text: _num(row.otherAllowanceAnnual));
    _employerPf = TextEditingController(text: _num(row.employerPfAnnual));
    _gratuity = TextEditingController(text: _num(row.gratuityAnnual));
    _ptax = TextEditingController(text: _num(row.professionalTaxMonthly));
    _c80 = TextEditingController(text: _num(row.deduction80CAnnual));
    _d80 = TextEditingController(text: _num(row.deduction80DAnnual));
    _homeLoan = TextEditingController(text: _num(row.homeLoanInterestAnnual));
    _otherDeductions = TextEditingController(
      text: _num(row.otherDeductionsAnnual),
    );
    _regime = row.taxRegime;
    _preview = row.tax;
    // Recompute the tax shortly after typing stops so finance sees it live.
    for (final controller in [
      _basic, _hra, _da, _special, _other, _ptax,
      _c80, _d80, _homeLoan, _otherDeductions,
    ]) {
      controller.addListener(_schedulePreview);
    }
  }

  void _schedulePreview() {
    _debounce?.cancel();
    _debounce = Timer(const Duration(milliseconds: 600), _refreshPreview);
  }

  @override
  void dispose() {
    _debounce?.cancel();
    _basic.dispose();
    _hra.dispose();
    _da.dispose();
    _special.dispose();
    _other.dispose();
    _employerPf.dispose();
    _gratuity.dispose();
    _ptax.dispose();
    _c80.dispose();
    _d80.dispose();
    _homeLoan.dispose();
    _otherDeductions.dispose();
    super.dispose();
  }

  double _parse(TextEditingController controller) =>
      double.tryParse(controller.text.trim()) ?? 0;

  Map<String, dynamic> _body() => {
    'basicAnnual': _parse(_basic),
    'hraAnnual': _parse(_hra),
    'daAnnual': _parse(_da),
    'specialAllowanceAnnual': _parse(_special),
    'otherAllowanceAnnual': _parse(_other),
    'employerPfAnnual': _parse(_employerPf),
    'gratuityAnnual': _parse(_gratuity),
    'professionalTaxMonthly': _parse(_ptax),
    'taxRegime': _regime,
    'deduction80CAnnual': _parse(_c80),
    'deduction80DAnnual': _parse(_d80),
    'homeLoanInterestAnnual': _parse(_homeLoan),
    'otherDeductionsAnnual': _parse(_otherDeductions),
  };

  Future<void> _refreshPreview() async {
    if (_previewBusy) return;
    setState(() => _previewBusy = true);
    try {
      final estimate = await ref
          .read(adminPayrollActionsProvider)
          .previewTax(_body());
      if (mounted) setState(() => _preview = estimate);
    } catch (_) {
      // Preview is best-effort; saving still validates server-side.
    } finally {
      if (mounted) setState(() => _previewBusy = false);
    }
  }

  Future<void> _save() async {
    setState(() => _busy = true);
    try {
      await ref
          .read(adminPayrollActionsProvider)
          .saveSalaryStructure(widget.initial.employeeId, _body());
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Pay details saved; tax recomputed')),
        );
        Navigator.pop(context, true);
      }
    } catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Could not save pay details: $error')),
        );
        setState(() => _busy = false);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final row = widget.initial;
    final bottom = MediaQuery.of(context).viewInsets.bottom;
    return Padding(
      padding: EdgeInsets.only(bottom: bottom),
      child: SingleChildScrollView(
        padding: const EdgeInsets.fromLTRB(16, 16, 16, 24),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(
              '${row.employeeName} · ${row.employeeNumber}',
              style: Theme.of(context).textTheme.titleMedium,
            ),
            if (row.department != null)
              Text(
                row.department!,
                style: Theme.of(context).textTheme.bodySmall,
              ),
            const SizedBox(height: 16),
            Text('Annual earnings (INR)', style: Theme.of(context).textTheme.titleSmall),
            const SizedBox(height: 8),
            _AmountField(label: 'Basic', controller: _basic),
            _AmountField(label: 'HRA', controller: _hra),
            _AmountField(label: 'DA', controller: _da),
            _AmountField(label: 'Special allowance', controller: _special),
            _AmountField(label: 'Other allowance', controller: _other),
            _AmountField(label: 'Employer PF (CTC)', controller: _employerPf),
            _AmountField(label: 'Gratuity (CTC)', controller: _gratuity),
            const SizedBox(height: 8),
            Text(
              'Statutory & tax',
              style: Theme.of(context).textTheme.titleSmall,
            ),
            const SizedBox(height: 8),
            _AmountField(
              label: 'Professional tax per month',
              controller: _ptax,
            ),
            const SizedBox(height: 8),
            DropdownButtonFormField<String>(
              initialValue: _regime,
              decoration: const InputDecoration(labelText: 'Income tax regime'),
              items: const [
                DropdownMenuItem(value: 'New', child: Text('New regime')),
                DropdownMenuItem(value: 'Old', child: Text('Old regime')),
              ],
              onChanged: (value) {
                setState(() => _regime = value ?? 'New');
                _refreshPreview();
              },
            ),
            if (_regime == 'Old') ...[
              const SizedBox(height: 8),
              Text(
                'Chapter VI-A declarations (old regime)',
                style: Theme.of(context).textTheme.titleSmall,
              ),
              const SizedBox(height: 8),
              _AmountField(label: '80C investments', controller: _c80),
              _AmountField(label: '80D medical insurance', controller: _d80),
              _AmountField(
                label: 'Home loan interest',
                controller: _homeLoan,
              ),
              _AmountField(
                label: 'Other deductions',
                controller: _otherDeductions,
              ),
            ],
            const SizedBox(height: 16),
            _TaxPreviewCard(estimate: _preview, busy: _previewBusy),
            const SizedBox(height: 16),
            Row(
              children: [
                Expanded(
                  child: AppButton(
                    label: 'Preview tax',
                    outlined: true,
                    busy: _previewBusy,
                    onPressed: _refreshPreview,
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: AppButton(
                    label: 'Save',
                    busy: _busy,
                    onPressed: _save,
                  ),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

class _AmountField extends StatelessWidget {
  const _AmountField({required this.label, required this.controller});

  final String label;
  final TextEditingController controller;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(bottom: 8),
    child: TextField(
      controller: controller,
      keyboardType: const TextInputType.numberWithOptions(decimal: true),
      decoration: InputDecoration(labelText: label),
    ),
  );
}

class _TaxPreviewCard extends StatelessWidget {
  const _TaxPreviewCard({required this.estimate, required this.busy});

  final PayrollTaxEstimate? estimate;
  final bool busy;

  @override
  Widget build(BuildContext context) {
    final tax = estimate;
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: busy && tax == null
            ? const Center(child: CircularProgressIndicator())
            : tax == null
            ? const Text('Enter pay details to see the computed tax.')
            : Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    'Computed income tax · FY ${tax.financialYear}-'
                    '${(tax.financialYear + 1) % 100} · ${tax.regime} regime',
                    style: Theme.of(context).textTheme.titleSmall,
                  ),
                  const SizedBox(height: 8),
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
                  _row(context, 'Health & education cess', AppFormatters.currency(tax.cess)),
                  const Divider(),
                  _row(context, 'Annual tax', AppFormatters.currency(tax.annualTax)),
                  _row(
                    context,
                    'Monthly TDS',
                    AppFormatters.currency(tax.monthlyTds),
                    emphasise: true,
                  ),
                ],
              ),
      ),
    );
  }

  Widget _row(
    BuildContext context,
    String label,
    String value, {
    bool emphasise = false,
  }) => Padding(
    padding: const EdgeInsets.symmetric(vertical: 2),
    child: Row(
      mainAxisAlignment: MainAxisAlignment.spaceBetween,
      children: [
        Text(label),
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
