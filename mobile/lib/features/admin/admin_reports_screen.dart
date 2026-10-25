import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/strings.dart';
import '../../core/utils/file_saver.dart';
import '../../models/directory_models.dart';
import '../../providers/admin_provider.dart';
import '../../providers/api_provider.dart';
import '../../providers/auth_provider.dart';
import '../../providers/directory_provider.dart';
import '../../widgets/app_button.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';

class AdminReportsScreen extends ConsumerStatefulWidget {
  const AdminReportsScreen({super.key});

  @override
  ConsumerState<AdminReportsScreen> createState() => _AdminReportsScreenState();
}

class _AdminReportsScreenState extends ConsumerState<AdminReportsScreen> {
  static const _reports = <String, String>{
    'headcount': 'Headcount & movement',
    'approval-aging': 'Approval aging',
    'attendance-exceptions': 'Attendance exceptions',
    'document-expiry': 'Document expiry',
    'leave-liability': 'Leave liability',
  };

  late DateTimeRange _range;
  String _report = 'headcount';
  String _exportFormat = 'csv';
  String? _departmentId;
  String? _locationId;
  bool _exporting = false;

  @override
  void initState() {
    super.initState();
    final today = DateUtils.dateOnly(DateTime.now());
    _range = DateTimeRange(
      start: DateTime(today.year - 1, today.month, today.day),
      end: today,
    );
  }

  String _date(DateTime value) =>
      '${value.year.toString().padLeft(4, '0')}-'
      '${value.month.toString().padLeft(2, '0')}-'
      '${value.day.toString().padLeft(2, '0')}';

  Future<void> _pickRange() async {
    final picked = await showDateRangePicker(
      context: context,
      firstDate: DateTime(2000),
      lastDate: DateTime.now().add(const Duration(days: 365)),
      initialDateRange: _range,
    );
    if (picked != null) setState(() => _range = picked);
  }

  Future<void> _export(Map<String, dynamic> payload) async {
    final rawRows = payload['rows'];
    final rows = rawRows is List
        ? rawRows.whereType<Map>().map(Map<String, dynamic>.from).toList()
        : <Map<String, dynamic>>[];
    if (rows.isEmpty) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('There are no rows to export.')),
      );
      return;
    }
    setState(() => _exporting = true);
    try {
      final bytes = await ref
          .read(apiClientProvider)
          .downloadBytes(
            '/reports/$_report/export',
            query: {
              'format': _exportFormat,
              'from': _date(_range.start),
              'to': _date(_range.end),
              if (_departmentId != null) 'departmentId': _departmentId,
              if (_locationId != null) 'locationId': _locationId,
            },
          );
      final path = await saveBytesToDevice(
        bytes,
        'wintech-${_report.replaceAll('-', '_')}-${_date(_range.end)}.$_exportFormat',
      );
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('${Strings.downloadedTo} $path')),
        );
      }
    } catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text('Export failed: $error')));
      }
    } finally {
      if (mounted) setState(() => _exporting = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final query = AdminReportQuery(
      report: _report,
      from: _date(_range.start),
      to: _date(_range.end),
      departmentId: _departmentId,
      locationId: _locationId,
    );
    final report = ref.watch(adminReportProvider(query));
    final canExport = ref
        .watch(authStateProvider)
        .hasPermission('report.export');
    final departments =
        ref.watch(departmentsProvider).value ?? const <OrgUnit>[];
    final locations =
        ref.watch(locationsProvider).value ?? const <LocationEntry>[];

    return AppScaffold(
      title: 'Workforce reports',
      body: Column(
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 8),
            child: Column(
              children: [
                DropdownButtonFormField<String>(
                  initialValue: _report,
                  decoration: const InputDecoration(labelText: 'Report'),
                  items: [
                    for (final entry in _reports.entries)
                      DropdownMenuItem(
                        value: entry.key,
                        child: Text(entry.value),
                      ),
                  ],
                  onChanged: (value) {
                    if (value != null) setState(() => _report = value);
                  },
                ),
                const SizedBox(height: 10),
                OutlinedButton.icon(
                  onPressed: _pickRange,
                  icon: const Icon(Icons.date_range_rounded),
                  label: Text('${_date(_range.start)} – ${_date(_range.end)}'),
                ),
                if (canExport) ...[
                  const SizedBox(height: 10),
                  DropdownButtonFormField<String>(
                    initialValue: _exportFormat,
                    decoration: const InputDecoration(
                      labelText: 'Export format',
                    ),
                    items: const [
                      DropdownMenuItem(value: 'csv', child: Text('CSV')),
                      DropdownMenuItem(
                        value: 'xlsx',
                        child: Text('Excel (.xlsx)'),
                      ),
                      DropdownMenuItem(value: 'pdf', child: Text('PDF')),
                    ],
                    onChanged: _exporting
                        ? null
                        : (value) {
                            if (value != null) {
                              setState(() => _exportFormat = value);
                            }
                          },
                  ),
                ],
                const SizedBox(height: 10),
                Row(
                  children: [
                    Expanded(
                      child: DropdownButtonFormField<String?>(
                        initialValue: _departmentId,
                        decoration: const InputDecoration(
                          labelText: Strings.department,
                          isDense: true,
                        ),
                        items: [
                          const DropdownMenuItem<String?>(
                            value: null,
                            child: Text('All'),
                          ),
                          for (final department in departments)
                            DropdownMenuItem<String?>(
                              value: department.id,
                              child: Text(
                                department.name,
                                overflow: TextOverflow.ellipsis,
                              ),
                            ),
                        ],
                        onChanged: (value) =>
                            setState(() => _departmentId = value),
                      ),
                    ),
                    const SizedBox(width: 10),
                    Expanded(
                      child: DropdownButtonFormField<String?>(
                        initialValue: _locationId,
                        decoration: const InputDecoration(
                          labelText: Strings.location,
                          isDense: true,
                        ),
                        items: [
                          const DropdownMenuItem<String?>(
                            value: null,
                            child: Text('All'),
                          ),
                          for (final location in locations)
                            DropdownMenuItem<String?>(
                              value: location.id,
                              child: Text(
                                location.name,
                                overflow: TextOverflow.ellipsis,
                              ),
                            ),
                        ],
                        onChanged: (value) =>
                            setState(() => _locationId = value),
                      ),
                    ),
                  ],
                ),
              ],
            ),
          ),
          Expanded(
            child: AsyncBody(
              value: report,
              onRetry: () => ref.invalidate(adminReportProvider(query)),
              isEmpty: (data) => (data['rows'] as List? ?? const []).isEmpty,
              emptyTitle: 'No report rows',
              emptyMessage: 'Try widening the date range or changing filters.',
              emptyIcon: Icons.query_stats_rounded,
              builder: (data) {
                final rawRows = data['rows'];
                final rows = rawRows is List
                    ? rawRows
                          .whereType<Map>()
                          .map(Map<String, dynamic>.from)
                          .toList()
                    : <Map<String, dynamic>>[];
                return ListView.separated(
                  physics: const AlwaysScrollableScrollPhysics(),
                  padding: const EdgeInsets.fromLTRB(16, 4, 16, 24),
                  itemCount: rows.length + 1,
                  separatorBuilder: (_, _) => const SizedBox(height: 8),
                  itemBuilder: (context, index) {
                    if (index == 0) {
                      if (!canExport) return const SizedBox.shrink();
                      return AppButton(
                        label: _exporting
                            ? Strings.saving
                            : 'Export ${_exportFormat.toUpperCase()}',
                        icon: Icons.download_outlined,
                        busy: _exporting,
                        onPressed: _exporting ? null : () => _export(data),
                      );
                    }
                    final row = rows[index - 1];
                    return Card(
                      child: Padding(
                        padding: const EdgeInsets.all(14),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            for (final entry in row.entries)
                              Padding(
                                padding: const EdgeInsets.symmetric(
                                  vertical: 3,
                                ),
                                child: Text(
                                  '${entry.key}: ${entry.value ?? '—'}',
                                ),
                              ),
                          ],
                        ),
                      ),
                    );
                  },
                );
              },
            ),
          ),
        ],
      ),
    );
  }
}
