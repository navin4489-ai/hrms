import 'dart:convert';

import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/strings.dart';
import '../../providers/admin_provider.dart';
import '../../providers/api_provider.dart';
import '../../providers/auth_provider.dart';
import '../../widgets/app_button.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';

final _payrollPeriodAdminPayslipsProvider =
    FutureProvider.family<List<dynamic>, String>(
      (ref, periodId) => ref
          .watch(apiClientProvider)
          .getList('/payroll-periods/$periodId/payslips'),
    );

class AdminPayrollImportScreen extends ConsumerStatefulWidget {
  const AdminPayrollImportScreen({
    super.key,
    required this.periodId,
    required this.periodLabel,
  });

  final String periodId;
  final String periodLabel;

  @override
  ConsumerState<AdminPayrollImportScreen> createState() =>
      _AdminPayrollImportScreenState();
}

class _AdminPayrollImportScreenState
    extends ConsumerState<AdminPayrollImportScreen> {
  static const _requiredHeaders = [
    'employeeNumber',
    'gross',
    'totalDeductions',
    'netPay',
    'currency',
  ];

  List<Map<String, dynamic>>? _rows;
  Map<String, dynamic>? _preview;
  int? _importedCount;
  bool _busy = false;
  String? _fileName;
  String? _error;

  List<List<String>> _parseCsv(String input) {
    final result = <List<String>>[];
    var row = <String>[];
    final field = StringBuffer();
    var quoted = false;
    for (var index = 0; index < input.length; index++) {
      final char = input[index];
      if (quoted) {
        if (char == '"' &&
            index + 1 < input.length &&
            input[index + 1] == '"') {
          field.write('"');
          index++;
        } else if (char == '"') {
          quoted = false;
        } else {
          field.write(char);
        }
      } else if (char == '"') {
        quoted = true;
      } else if (char == ',') {
        row.add(field.toString());
        field.clear();
      } else if (char == '\n' || char == '\r') {
        row.add(field.toString());
        field.clear();
        if (row.any((item) => item.trim().isNotEmpty)) result.add(row);
        row = [];
        if (char == '\r' &&
            index + 1 < input.length &&
            input[index + 1] == '\n')
          index++;
      } else {
        field.write(char);
      }
    }
    row.add(field.toString());
    if (row.any((item) => item.trim().isNotEmpty)) result.add(row);
    return result;
  }

  List<Map<String, dynamic>> _componentLines(String? value, String kind) {
    if (value == null || value.trim().isEmpty) return [];
    return value.split(';').where((line) => line.trim().isNotEmpty).map((line) {
      final separator = line.lastIndexOf(':');
      if (separator < 1)
        throw FormatException('Component must be Name:Amount: $line');
      final amount = double.tryParse(line.substring(separator + 1).trim());
      if (amount == null || amount < 0)
        throw FormatException('Invalid component amount: $line');
      return {
        'component': line.substring(0, separator).trim(),
        'amount': amount,
        'kind': kind,
      };
    }).toList();
  }

  Future<void> _pickFile() async {
    final file = await FilePicker.pickFile(
      type: FileType.custom,
      allowedExtensions: const ['csv'],
    );
    if (file == null || !mounted) return;
    final bytesLength = await file.length();
    if (bytesLength == null || bytesLength > 5 * 1024 * 1024) {
      setState(() => _error = 'CSV file must be smaller than 5 MB.');
      return;
    }
    setState(() {
      _busy = true;
      _error = null;
      _preview = null;
      _importedCount = null;
    });
    try {
      final table = _parseCsv(
        utf8.decode(await file.readAsBytes(), allowMalformed: true),
      );
      if (table.length < 2 || table.length > 1001) {
        throw const FormatException('Import must contain 1–1000 payslip rows.');
      }
      final headers = table.first.map((item) => item.trim()).toList();
      final missing = _requiredHeaders
          .where((header) => !headers.contains(header))
          .toList();
      if (missing.isNotEmpty)
        throw FormatException('Missing columns: ${missing.join(', ')}');
      final rows = <Map<String, dynamic>>[];
      for (final raw in table.skip(1)) {
        final values = <String, String>{};
        for (var i = 0; i < headers.length; i++) {
          values[headers[i]] = i < raw.length ? raw[i].trim() : '';
        }
        final gross = double.tryParse(values['gross'] ?? '');
        final deductions = double.tryParse(values['totalDeductions'] ?? '');
        final net = double.tryParse(values['netPay'] ?? '');
        if ((values['employeeNumber'] ?? '').isEmpty ||
            gross == null ||
            deductions == null ||
            net == null) {
          throw const FormatException(
            'Each row requires employeeNumber and numeric gross, totalDeductions and netPay values.',
          );
        }
        rows.add({
          'employeeNumber': values['employeeNumber'],
          'gross': gross,
          'totalDeductions': deductions,
          'netPay': net,
          'currency': (values['currency']?.isEmpty ?? true)
              ? 'INR'
              : values['currency']!.toUpperCase(),
          'components': [
            ..._componentLines(values['earnings'], 'Earning'),
            ..._componentLines(values['deductions'], 'Deduction'),
          ],
        });
      }
      setState(() {
        _fileName = file.name;
        _rows = rows;
      });
    } catch (error) {
      setState(() => _error = 'CSV preview failed: $error');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _previewImport() async {
    if (_rows == null) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final result = await ref
          .read(apiClientProvider)
          .postMap(
            '/payroll-periods/${widget.periodId}/payslips/preview',
            data: {'rows': _rows},
          );
      setState(() => _preview = result);
    } catch (error) {
      setState(() => _error = 'Server validation failed: $error');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _import() async {
    if (_rows == null || _preview?['validRows'] != _preview?['totalRows'])
      return;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final result = await ref
          .read(apiClientProvider)
          .postMap(
            '/payroll-periods/${widget.periodId}/payslips/import',
            data: {'rows': _rows},
          );
      setState(
        () => _importedCount = (result['importedCount'] as num?)?.toInt() ?? 0,
      );
      ref.invalidate(adminPayrollPeriodsProvider);
      ref.invalidate(_payrollPeriodAdminPayslipsProvider(widget.periodId));
    } catch (error) {
      setState(() => _error = 'Import failed: $error');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _publish() async {
    final reason = TextEditingController();
    final savedReason = await showDialog<String>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('Publish payslips'),
        content: TextField(
          controller: reason,
          minLines: 2,
          maxLines: 4,
          decoration: const InputDecoration(
            labelText: 'Reason for publication',
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(dialogContext),
            child: const Text(Strings.cancel),
          ),
          FilledButton(
            onPressed: () {
              if (reason.text.trim().isNotEmpty)
                Navigator.pop(dialogContext, reason.text.trim());
            },
            child: const Text('Publish'),
          ),
        ],
      ),
    );
    reason.dispose();
    if (savedReason == null || !mounted) return;
    setState(() => _busy = true);
    try {
      final result = await ref
          .read(apiClientProvider)
          .postMap(
            '/payroll-periods/${widget.periodId}/payslips/publish',
            data: {'reason': savedReason},
          );
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text('${result['publishedCount']} payslip(s) published'),
          ),
        );
      }
      ref.invalidate(adminPayrollPeriodsProvider);
      ref.invalidate(_payrollPeriodAdminPayslipsProvider(widget.periodId));
    } catch (error) {
      if (mounted) setState(() => _error = 'Publication failed: $error');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final canPublish = ref
        .watch(authStateProvider)
        .hasPermission('payroll.publish');
    final rows = _preview?['rows'] is List
        ? (_preview!['rows'] as List).whereType<Map>().toList()
        : <Map>[];
    final validRows = (_preview?['validRows'] as num?)?.toInt() ?? 0;
    final totalRows = (_preview?['totalRows'] as num?)?.toInt() ?? 0;
    final periodPayslips = ref.watch(
      _payrollPeriodAdminPayslipsProvider(widget.periodId),
    );
    return AppScaffold(
      title: 'Payroll · ${widget.periodLabel}',
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          const Text(
            'Import payroll output as draft payslips, review server-side exceptions, then publish to employees. Payroll calculation is performed by the designated payroll engine.',
          ),
          const SizedBox(height: 12),
          FilledButton.icon(
            onPressed: _busy ? null : _pickFile,
            icon: const Icon(Icons.upload_file_outlined),
            label: Text(_fileName ?? 'Choose payroll CSV'),
          ),
          const SizedBox(height: 8),
          const Text(
            'Required columns: employeeNumber,gross,totalDeductions,netPay,currency. Optional earnings/deductions use Name:Amount;Name:Amount.',
          ),
          if (_error != null) ...[
            const SizedBox(height: 12),
            Text(
              _error!,
              style: TextStyle(color: Theme.of(context).colorScheme.error),
            ),
          ],
          if (_rows != null) ...[
            const SizedBox(height: 12),
            AppButton(
              label: 'Validate import',
              icon: Icons.fact_check_outlined,
              busy: _busy,
              onPressed: _busy ? null : _previewImport,
            ),
          ],
          if (_preview != null) ...[
            const SizedBox(height: 12),
            Card(
              child: ListTile(
                leading: Icon(
                  validRows == totalRows
                      ? Icons.check_circle_outline
                      : Icons.warning_amber_rounded,
                ),
                title: Text('$validRows / $totalRows valid rows'),
                subtitle: const Text(
                  'A duplicate payslip or inconsistent totals blocks the batch.',
                ),
              ),
            ),
            for (final row in rows)
              if (row['valid'] != true)
                ListTile(
                  dense: true,
                  leading: const Icon(Icons.error_outline_rounded),
                  title: Text('Row ${row['row']} · ${row['employeeNumber']}'),
                  subtitle: Text(
                    (row['errors'] as List? ?? const []).join(' · '),
                  ),
                ),
            if (_importedCount == null && validRows == totalRows)
              AppButton(
                label: 'Import $totalRows draft payslips',
                icon: Icons.save_alt_rounded,
                busy: _busy,
                onPressed: _busy ? null : _import,
              ),
          ],
          if (_importedCount != null) ...[
            const SizedBox(height: 12),
            Card(
              child: ListTile(
                leading: const Icon(Icons.check_circle_outline),
                title: Text('$_importedCount draft payslip(s) imported'),
              ),
            ),
          ],
          AsyncBody(
            value: periodPayslips,
            onRetry: () => ref.invalidate(
              _payrollPeriodAdminPayslipsProvider(widget.periodId),
            ),
            isEmpty: (items) => items.isEmpty,
            emptyTitle: 'No payslips in this period',
            emptyMessage:
                'Validate and import payroll output to create drafts.',
            builder: (items) {
              final drafts = items
                  .whereType<Map>()
                  .where((item) => item['status'] == 'Draft')
                  .toList();
              final published = items
                  .whereType<Map>()
                  .where((item) => item['status'] == 'Published')
                  .length;
              return Card(
                child: Column(
                  children: [
                    ListTile(
                      leading: const Icon(Icons.receipt_long_outlined),
                      title: Text(
                        '${items.length} payslip(s) · ${drafts.length} draft · $published published',
                      ),
                      subtitle: const Text(
                        'Imported output is not visible to employees until published.',
                      ),
                    ),
                    if (drafts.isNotEmpty && canPublish)
                      Padding(
                        padding: const EdgeInsets.fromLTRB(12, 0, 12, 12),
                        child: AppButton(
                          label: 'Publish ${drafts.length} draft payslip(s)',
                          icon: Icons.publish_outlined,
                          onPressed: _busy ? null : _publish,
                          busy: _busy,
                        ),
                      ),
                  ],
                ),
              );
            },
          ),
        ],
      ),
    );
  }
}
