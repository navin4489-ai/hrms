import 'dart:convert';

import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/strings.dart';
import '../../core/utils/file_saver.dart';
import '../../providers/api_provider.dart';
import '../../providers/directory_provider.dart';
import '../../widgets/app_button.dart';
import '../../widgets/app_scaffold.dart';

class _ImportRow {
  _ImportRow(this.values, this.line);
  final Map<String, String> values;
  final int line;
  final List<String> errors = [];
  bool imported = false;
  String? importError;
}

class AdminEmployeeImportScreen extends ConsumerStatefulWidget {
  const AdminEmployeeImportScreen({super.key});

  @override
  ConsumerState<AdminEmployeeImportScreen> createState() =>
      _AdminEmployeeImportScreenState();
}

class _AdminEmployeeImportScreenState
    extends ConsumerState<AdminEmployeeImportScreen> {
  static const _headers = [
    'employeeNumber',
    'firstName',
    'lastName',
    'workEmail',
    'phone',
    'joinDate',
    'legalEntityCode',
    'departmentCode',
    'locationCode',
    'designationCode',
    'managerEmployeeNumber',
    'employmentType',
  ];

  final List<_ImportRow> _rows = [];
  bool _loading = false;
  bool _committing = false;

  List<List<String>> _parseCsv(String input) {
    final rows = <List<String>>[];
    var row = <String>[];
    final field = StringBuffer();
    var quoted = false;
    for (var i = 0; i < input.length; i++) {
      final char = input[i];
      if (quoted) {
        if (char == '"') {
          if (i + 1 < input.length && input[i + 1] == '"') {
            field.write('"');
            i++;
          } else {
            quoted = false;
          }
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
        if (row.any((value) => value.trim().isNotEmpty)) rows.add(row);
        row = [];
        if (char == '\r' && i + 1 < input.length && input[i + 1] == '\n') i++;
      } else {
        field.write(char);
      }
    }
    row.add(field.toString());
    if (row.any((value) => value.trim().isNotEmpty)) rows.add(row);
    return rows;
  }

  Future<void> _downloadTemplate() async {
    final csv =
        '${_headers.join(',')}\r\n'
        ',First name,Last name,employee@company.com,,2026-01-01,ENTITY,DEPT,LOC,DESG,,FullTime\r\n';
    try {
      final path = await saveBytesToDevice(
        utf8.encode(csv),
        'wintech-employee-import-template.csv',
      );
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('${Strings.downloadedTo} $path')),
        );
      }
    } catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Template download failed: $error')),
        );
      }
    }
  }

  Future<void> _pickCsv() async {
    final file = await FilePicker.pickFile(
      type: FileType.custom,
      allowedExtensions: const ['csv'],
    );
    if (file == null || !mounted) return;
    final size = await file.length();
    if (size == null || size > 5 * 1024 * 1024) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('CSV file must be smaller than 5 MB.')),
      );
      return;
    }
    setState(() {
      _loading = true;
      _rows.clear();
    });
    try {
      final parsed = _parseCsv(
        utf8.decode(await file.readAsBytes(), allowMalformed: true),
      );
      if (parsed.length < 2)
        throw const FormatException('The CSV contains no employee rows.');
      final header = parsed.first.map((value) => value.trim()).toList();
      final missing = _headers
          .where((column) => !header.contains(column))
          .toList();
      if (missing.isNotEmpty) {
        throw FormatException('Missing columns: ${missing.join(', ')}');
      }
      if (parsed.length > 501) {
        throw const FormatException(
          'Import is limited to 500 employee rows per batch.',
        );
      }

      final options = await ref.read(employeeCreateOptionsProvider.future);
      final current = await ref
          .read(apiClientProvider)
          .getMap('/employees', query: {'page': 1, 'pageSize': 500});
      final existingRows = current['items'] is List
          ? (current['items'] as List).whereType<Map>().toList()
          : <Map>[];
      final employeeNumbers = existingRows
          .map((item) => item['employeeNumber']?.toString().toLowerCase())
          .whereType<String>()
          .toSet();
      final emails = existingRows
          .map((item) => item['workEmail']?.toString().toLowerCase())
          .whereType<String>()
          .toSet();
      final seenNumbers = <String>{};
      final seenEmails = <String>{};

      for (var index = 1; index < parsed.length; index++) {
        final values = <String, String>{};
        for (var column = 0; column < header.length; column++) {
          values[header[column]] = column < parsed[index].length
              ? parsed[index][column].trim()
              : '';
        }
        final row = _ImportRow(values, index + 1);
        void required(String key, String label) {
          if ((values[key] ?? '').isEmpty) row.errors.add('$label is required');
        }

        required('firstName', 'First name');
        required('lastName', 'Last name');
        required('workEmail', 'Work email');
        required('joinDate', 'Join date');
        final email = (values['workEmail'] ?? '').toLowerCase();
        if (email.isNotEmpty &&
            !RegExp(r'^[^\s@]+@[^\s@]+\.[^\s@]+$').hasMatch(email)) {
          row.errors.add('Work email is invalid');
        }
        if (email.isNotEmpty &&
            (emails.contains(email) || !seenEmails.add(email))) {
          row.errors.add('Work email is duplicated');
        }
        final number = (values['employeeNumber'] ?? '').toLowerCase();
        if (number.isNotEmpty &&
            (employeeNumbers.contains(number) || !seenNumbers.add(number))) {
          row.errors.add('Employee number is duplicated');
        }
        if (DateTime.tryParse(values['joinDate'] ?? '') == null) {
          row.errors.add('Join date must be YYYY-MM-DD');
        }
        final entityCode = (values['legalEntityCode'] ?? '').toLowerCase();
        final departmentCode = (values['departmentCode'] ?? '').toLowerCase();
        final locationCode = (values['locationCode'] ?? '').toLowerCase();
        final designationCode = (values['designationCode'] ?? '').toLowerCase();
        if (!options.legalEntities.any(
          (item) => item.code?.toLowerCase() == entityCode,
        )) {
          row.errors.add('Legal entity code was not found');
        }
        if (!options.departments.any(
          (item) => item.code?.toLowerCase() == departmentCode,
        )) {
          row.errors.add('Department code was not found');
        }
        if (!options.locations.any(
          (item) => item.code?.toLowerCase() == locationCode,
        )) {
          row.errors.add('Location code was not found');
        }
        if (!options.designations.any(
          (item) => item.code?.toLowerCase() == designationCode,
        )) {
          row.errors.add('Designation code was not found');
        }
        final managerNumber = (values['managerEmployeeNumber'] ?? '')
            .toLowerCase();
        if (managerNumber.isNotEmpty &&
            !options.managers.any(
              (item) => item.employeeNumber.toLowerCase() == managerNumber,
            )) {
          row.errors.add('Reporting manager employee number was not found');
        }
        const employmentTypes = {'fulltime', 'parttime', 'contract', 'intern'};
        final employmentType = (values['employmentType'] ?? 'FullTime')
            .toLowerCase();
        if (!employmentTypes.contains(employmentType)) {
          row.errors.add(
            'Employment type must be FullTime, PartTime, Contract or Intern',
          );
        }
        _rows.add(row);
      }
      if (mounted) setState(() {});
    } catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text('CSV preview failed: $error')));
      }
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _commit() async {
    final invalid = _rows.where((row) => row.errors.isNotEmpty).length;
    if (_rows.isEmpty || invalid > 0) return;
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('Commit employee import?'),
        content: Text(
          '${_rows.length} employee record(s) will be created. Each row commits separately; successful rows are not rolled back if another row fails.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(dialogContext, false),
            child: const Text(Strings.cancel),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(dialogContext, true),
            child: const Text('Import employees'),
          ),
        ],
      ),
    );
    if (confirmed != true || !mounted) return;
    setState(() => _committing = true);
    final options = await ref.read(employeeCreateOptionsProvider.future);
    for (final row in _rows.where((item) => !item.imported)) {
      try {
        final values = row.values;
        final managerNumber = values['managerEmployeeNumber']?.toLowerCase();
        final manager = managerNumber == null || managerNumber.isEmpty
            ? null
            : options.managers
                  .where(
                    (item) =>
                        item.employeeNumber.toLowerCase() == managerNumber,
                  )
                  .firstOrNull;
        await ref
            .read(apiClientProvider)
            .postMap(
              '/employees',
              data: {
                if ((values['employeeNumber'] ?? '').isNotEmpty)
                  'employeeNumber': values['employeeNumber'],
                'firstName': values['firstName'],
                'lastName': values['lastName'],
                'workEmail': values['workEmail'],
                if ((values['phone'] ?? '').isNotEmpty)
                  'phone': values['phone'],
                'joinDate': values['joinDate'],
                'legalEntityId': options.legalEntities
                    .firstWhere(
                      (item) =>
                          item.code?.toLowerCase() ==
                          values['legalEntityCode']!.toLowerCase(),
                    )
                    .id,
                'departmentId': options.departments
                    .firstWhere(
                      (item) =>
                          item.code?.toLowerCase() ==
                          values['departmentCode']!.toLowerCase(),
                    )
                    .id,
                'locationId': options.locations
                    .firstWhere(
                      (item) =>
                          item.code?.toLowerCase() ==
                          values['locationCode']!.toLowerCase(),
                    )
                    .id,
                'designationId': options.designations
                    .firstWhere(
                      (item) =>
                          item.code?.toLowerCase() ==
                          values['designationCode']!.toLowerCase(),
                    )
                    .id,
                if (manager != null) 'managerId': manager.employeeId,
                'employmentType': values['employmentType']?.isNotEmpty == true
                    ? values['employmentType']
                    : 'FullTime',
              },
            );
        row.imported = true;
      } catch (error) {
        row.importError = error.toString();
      }
      if (mounted) setState(() {});
    }
    ref.invalidate(directoryProvider);
    if (mounted) setState(() => _committing = false);
  }

  @override
  Widget build(BuildContext context) {
    final validCount = _rows.where((row) => row.errors.isEmpty).length;
    final errorCount = _rows
        .where((row) => row.errors.isNotEmpty || row.importError != null)
        .length;
    final importedCount = _rows.where((row) => row.imported).length;
    return AppScaffold(
      title: 'Employee CSV import',
      body: Column(
        children: [
          Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                const Text(
                  'Upload a UTF-8 CSV using reference codes. Preview validation runs before any employee is created.',
                ),
                const SizedBox(height: 10),
                OutlinedButton.icon(
                  onPressed: _downloadTemplate,
                  icon: const Icon(Icons.download_outlined),
                  label: const Text('Download CSV template'),
                ),
                const SizedBox(height: 8),
                FilledButton.icon(
                  onPressed: _loading || _committing ? null : _pickCsv,
                  icon: const Icon(Icons.upload_file_outlined),
                  label: Text(_loading ? 'Validating…' : 'Choose CSV'),
                ),
                if (_rows.isNotEmpty) ...[
                  const SizedBox(height: 12),
                  Text(
                    '${_rows.length} rows · $validCount valid · $errorCount with errors · $importedCount imported',
                  ),
                  if (errorCount == 0 && importedCount < _rows.length)
                    Padding(
                      padding: const EdgeInsets.only(top: 8),
                      child: AppButton(
                        label: _committing ? 'Importing…' : 'Commit import',
                        busy: _committing,
                        onPressed: _committing ? null : _commit,
                      ),
                    ),
                ],
              ],
            ),
          ),
          Expanded(
            child: _rows.isEmpty
                ? const Center(child: Text('No CSV preview loaded.'))
                : ListView.separated(
                    padding: const EdgeInsets.fromLTRB(16, 0, 16, 20),
                    itemCount: _rows.length,
                    separatorBuilder: (_, _) => const SizedBox(height: 6),
                    itemBuilder: (context, index) {
                      final row = _rows[index];
                      final name =
                          '${row.values['firstName'] ?? ''} ${row.values['lastName'] ?? ''}'
                              .trim();
                      final messages = [
                        ...row.errors,
                        if (row.importError != null) row.importError!,
                        if (row.imported) 'Imported successfully',
                      ];
                      return Card(
                        child: ListTile(
                          leading: Icon(
                            row.imported
                                ? Icons.check_circle_outline
                                : row.errors.isEmpty
                                ? Icons.check_circle_outline
                                : Icons.error_outline,
                            color: row.imported
                                ? Colors.green
                                : row.errors.isEmpty
                                ? null
                                : Theme.of(context).colorScheme.error,
                          ),
                          title: Text(
                            'Row ${row.line} · ${name.isEmpty ? 'Employee' : name}',
                          ),
                          subtitle: Text(
                            messages.isEmpty
                                ? 'Ready to import'
                                : messages.join(' · '),
                          ),
                        ),
                      );
                    },
                  ),
          ),
        ],
      ),
    );
  }
}
