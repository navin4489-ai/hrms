import 'dart:convert';
import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/strings.dart';
import '../../core/utils/file_saver.dart';
import '../../models/directory_models.dart';
import '../../providers/admin_attendance_provider.dart';
import '../../providers/admin_provider.dart';
import '../../providers/api_provider.dart';
import '../../providers/auth_provider.dart';
import '../../providers/directory_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';

class AdminAttendanceDashboardScreen extends ConsumerStatefulWidget {
  const AdminAttendanceDashboardScreen({super.key});

  @override
  ConsumerState<AdminAttendanceDashboardScreen> createState() =>
      _AdminAttendanceDashboardScreenState();
}

class _AdminAttendanceDashboardScreenState
    extends ConsumerState<AdminAttendanceDashboardScreen> {
  late DateTimeRange _range;
  String? _departmentId;
  String? _locationId;

  @override
  void initState() {
    super.initState();
    final now = DateUtils.dateOnly(DateTime.now());
    _range = DateTimeRange(start: DateTime(now.year, now.month, 1), end: now);
  }

  String _date(DateTime d) =>
      '${d.year.toString().padLeft(4, '0')}-'
      '${d.month.toString().padLeft(2, '0')}-'
      '${d.day.toString().padLeft(2, '0')}';

  AdminAttendanceQuery get _query => AdminAttendanceQuery(
    from: _date(_range.start),
    to: _date(_range.end),
    departmentId: _departmentId,
    locationId: _locationId,
  );

  Future<void> _pickRange() async {
    final picked = await showDateRangePicker(
      context: context,
      firstDate: DateTime(2000),
      lastDate: DateTime.now().add(const Duration(days: 1)),
      initialDateRange: _range,
    );
    if (picked != null) setState(() => _range = picked);
  }

  Future<void> _exportDaily(Map<String, dynamic> data) async {
    final daily = (data['daily'] as List? ?? const [])
        .whereType<Map>()
        .toList();
    if (daily.isEmpty) {
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(const SnackBar(content: Text('No daily data to export.')));
      return;
    }
    final csv = StringBuffer('date,present,absent,late,onLeave\r\n');
    for (final row in daily) {
      csv.writeln(
        '${row['date']},${row['present']},${row['absent']},'
        '${row['late']},${row['onLeave']}',
      );
    }
    try {
      final path = await saveBytesToDevice(
        utf8.encode(csv.toString()),
        'wintech-attendance-${_date(_range.end)}.csv',
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
    }
  }

  @override
  Widget build(BuildContext context) {
    final dashboard = ref.watch(adminAttendanceDashboardProvider(_query));
    final departments =
        ref.watch(departmentsProvider).value ?? const <OrgUnit>[];
    final locations =
        ref.watch(locationsProvider).value ?? const <LocationEntry>[];

    return DefaultTabController(
      length: 3,
      child: AppScaffold(
        title: 'Attendance dashboard',
        bottom: const TabBar(
          isScrollable: true,
          tabs: [
            Tab(text: 'Overview'),
            Tab(text: 'Capture log'),
            Tab(text: 'Settings'),
          ],
        ),
        body: TabBarView(
          children: [
            // ----------------------------------------------------- overview
            Column(
              children: [
                _FilterBar(
                  rangeLabel: '${_date(_range.start)} – ${_date(_range.end)}',
                  onPickRange: _pickRange,
                  departmentId: _departmentId,
                  locationId: _locationId,
                  departments: departments,
                  locations: locations,
                  onDepartment: (v) => setState(() => _departmentId = v),
                  onLocation: (v) => setState(() => _locationId = v),
                ),
                Expanded(
                  child: AsyncBody(
                    value: dashboard,
                    onRetry: () => ref.invalidate(
                      adminAttendanceDashboardProvider(_query),
                    ),
                    builder: (data) => _Overview(
                      data: data,
                      onExport: () => _exportDaily(data),
                    ),
                  ),
                ),
              ],
            ),
            // -------------------------------------------------- capture log
            _CaptureLog(query: _query),
            // ----------------------------------------------------- settings
            const _CaptureSettingsTab(),
          ],
        ),
      ),
    );
  }
}

class _FilterBar extends StatelessWidget {
  const _FilterBar({
    required this.rangeLabel,
    required this.onPickRange,
    required this.departmentId,
    required this.locationId,
    required this.departments,
    required this.locations,
    required this.onDepartment,
    required this.onLocation,
  });

  final String rangeLabel;
  final VoidCallback onPickRange;
  final String? departmentId;
  final String? locationId;
  final List<OrgUnit> departments;
  final List<LocationEntry> locations;
  final ValueChanged<String?> onDepartment;
  final ValueChanged<String?> onLocation;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 12, 16, 6),
      child: Column(
        children: [
          OutlinedButton.icon(
            onPressed: onPickRange,
            icon: const Icon(Icons.date_range_rounded),
            label: Text(rangeLabel),
          ),
          const SizedBox(height: 10),
          Row(
            children: [
              Expanded(
                child: DropdownButtonFormField<String?>(
                  initialValue: departmentId,
                  isExpanded: true,
                  decoration: const InputDecoration(
                    labelText: Strings.department,
                    isDense: true,
                  ),
                  items: [
                    const DropdownMenuItem<String?>(
                      value: null,
                      child: Text('All'),
                    ),
                    for (final d in departments)
                      DropdownMenuItem<String?>(
                        value: d.id,
                        child: Text(d.name, overflow: TextOverflow.ellipsis),
                      ),
                  ],
                  onChanged: onDepartment,
                ),
              ),
              const SizedBox(width: 10),
              Expanded(
                child: DropdownButtonFormField<String?>(
                  initialValue: locationId,
                  isExpanded: true,
                  decoration: const InputDecoration(
                    labelText: Strings.location,
                    isDense: true,
                  ),
                  items: [
                    const DropdownMenuItem<String?>(
                      value: null,
                      child: Text('All'),
                    ),
                    for (final l in locations)
                      DropdownMenuItem<String?>(
                        value: l.id,
                        child: Text(l.name, overflow: TextOverflow.ellipsis),
                      ),
                  ],
                  onChanged: onLocation,
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class _Overview extends StatelessWidget {
  const _Overview({required this.data, required this.onExport});

  final Map<String, dynamic> data;
  final VoidCallback onExport;

  @override
  Widget build(BuildContext context) {
    final totals = Map<String, dynamic>.from(data['totals'] as Map? ?? {});
    final daily = (data['daily'] as List? ?? const [])
        .whereType<Map>()
        .toList();
    final status = (data['statusBreakdown'] as List? ?? const [])
        .whereType<Map>()
        .toList();
    final leave = (data['leaveByType'] as List? ?? const [])
        .whereType<Map>()
        .toList();
    final byDept = (data['byDepartment'] as List? ?? const [])
        .whereType<Map>()
        .toList();

    return ListView(
      padding: const EdgeInsets.fromLTRB(16, 4, 16, 24),
      children: [
        Wrap(
          spacing: 10,
          runSpacing: 10,
          children: [
            _Metric(
              'Employees',
              '${totals['employees'] ?? 0}',
              Icons.groups_outlined,
            ),
            _Metric(
              'Present',
              '${totals['present'] ?? 0}',
              Icons.check_circle_outline,
              Colors.green,
            ),
            _Metric(
              'Absent',
              '${totals['absent'] ?? 0}',
              Icons.cancel_outlined,
              Colors.red,
            ),
            _Metric(
              'Late',
              '${totals['late'] ?? 0}',
              Icons.schedule_rounded,
              Colors.orange,
            ),
            _Metric(
              'On leave',
              '${totals['onLeave'] ?? 0}',
              Icons.beach_access_outlined,
              Colors.blue,
            ),
            _Metric(
              'Pending corrections',
              '${totals['pendingCorrections'] ?? 0}',
              Icons.edit_note_rounded,
            ),
            _Metric(
              'Worked hours',
              '${totals['totalWorkedHours'] ?? 0}',
              Icons.timelapse_rounded,
            ),
            _Metric(
              'Overtime',
              '${totals['totalOvertimeHours'] ?? 0}',
              Icons.more_time_rounded,
            ),
          ],
        ),
        const SizedBox(height: 18),
        Row(
          children: [
            Expanded(
              child: Text(
                'Daily attendance',
                style: Theme.of(context).textTheme.titleMedium,
              ),
            ),
            TextButton.icon(
              onPressed: onExport,
              icon: const Icon(Icons.download_outlined),
              label: const Text('Export CSV'),
            ),
          ],
        ),
        const SizedBox(height: 8),
        _BarChart(
          rows: [
            for (final d in daily)
              _Bar(
                label: (d['date']?.toString() ?? '').replaceFirst(
                  RegExp(r'^\d{4}-'),
                  '',
                ),
                segments: [
                  _Seg((d['present'] as num?)?.toDouble() ?? 0, Colors.green),
                  _Seg((d['late'] as num?)?.toDouble() ?? 0, Colors.orange),
                  _Seg((d['onLeave'] as num?)?.toDouble() ?? 0, Colors.blue),
                  _Seg((d['absent'] as num?)?.toDouble() ?? 0, Colors.red),
                ],
              ),
          ],
          legend: const {
            'Present': Colors.green,
            'Late': Colors.orange,
            'Leave': Colors.blue,
            'Absent': Colors.red,
          },
        ),
        const SizedBox(height: 18),
        Text(
          'Status breakdown',
          style: Theme.of(context).textTheme.titleMedium,
        ),
        const SizedBox(height: 8),
        _SimpleBars(
          items: [
            for (final s in status)
              MapEntry(
                s['status']?.toString() ?? '',
                (s['count'] as num?)?.toDouble() ?? 0,
              ),
          ],
          color: Theme.of(context).colorScheme.primary,
        ),
        const SizedBox(height: 18),
        Text('Leave by type', style: Theme.of(context).textTheme.titleMedium),
        const SizedBox(height: 8),
        if (leave.isEmpty)
          const Text('No approved leave in this range.')
        else
          _SimpleBars(
            items: [
              for (final l in leave)
                MapEntry(
                  '${l['leaveType']} (${l['requests']})',
                  (l['days'] as num?)?.toDouble() ?? 0,
                ),
            ],
            color: Colors.blue,
          ),
        const SizedBox(height: 18),
        Text('By department', style: Theme.of(context).textTheme.titleMedium),
        const SizedBox(height: 8),
        for (final d in byDept)
          Card(
            child: ListTile(
              title: Text(d['department']?.toString() ?? 'Unassigned'),
              subtitle: Text(
                'Present ${d['present']} · Late ${d['late']} · Absent ${d['absent']}',
              ),
            ),
          ),
      ],
    );
  }
}

class _Metric extends StatelessWidget {
  const _Metric(this.label, this.value, this.icon, [this.color]);

  final String label;
  final String value;
  final IconData icon;
  final Color? color;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      width: 160,
      child: Card(
        child: Padding(
          padding: const EdgeInsets.all(12),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Icon(icon, color: color ?? Theme.of(context).colorScheme.primary),
              const SizedBox(height: 6),
              Text(value, style: Theme.of(context).textTheme.titleLarge),
              Text(label, style: Theme.of(context).textTheme.bodySmall),
            ],
          ),
        ),
      ),
    );
  }
}

class _Seg {
  const _Seg(this.value, this.color);
  final double value;
  final Color color;
}

class _Bar {
  const _Bar({required this.label, required this.segments});
  final String label;
  final List<_Seg> segments;
}

class _BarChart extends StatelessWidget {
  const _BarChart({required this.rows, required this.legend});

  final List<_Bar> rows;
  final Map<String, Color> legend;

  @override
  Widget build(BuildContext context) {
    if (rows.isEmpty) return const Text('No data in this range.');
    final maxTotal = rows
        .map((r) => r.segments.fold<double>(0, (a, s) => a + s.value))
        .fold<double>(1, (a, b) => b > a ? b : a);
    return Column(
      children: [
        Wrap(
          spacing: 12,
          children: [
            for (final entry in legend.entries)
              Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Container(width: 12, height: 12, color: entry.value),
                  const SizedBox(width: 4),
                  Text(entry.key, style: Theme.of(context).textTheme.bodySmall),
                ],
              ),
          ],
        ),
        const SizedBox(height: 10),
        for (final row in rows)
          Padding(
            padding: const EdgeInsets.symmetric(vertical: 2),
            child: Row(
              children: [
                SizedBox(
                  width: 44,
                  child: Text(
                    row.label,
                    style: Theme.of(context).textTheme.bodySmall,
                  ),
                ),
                Expanded(
                  child: SizedBox(
                    height: 16,
                    child: Row(
                      children: [
                        for (final seg in row.segments)
                          if (seg.value > 0)
                            Expanded(
                              flex: (seg.value / maxTotal * 1000).round(),
                              child: Container(color: seg.color),
                            ),
                        Expanded(
                          flex:
                              ((maxTotal -
                                          row.segments.fold<double>(
                                            0,
                                            (a, s) => a + s.value,
                                          )) /
                                      maxTotal *
                                      1000)
                                  .round()
                                  .clamp(0, 1000),
                          child: const SizedBox(),
                        ),
                      ],
                    ),
                  ),
                ),
              ],
            ),
          ),
      ],
    );
  }
}

class _SimpleBars extends StatelessWidget {
  const _SimpleBars({required this.items, required this.color});

  final List<MapEntry<String, double>> items;
  final Color color;

  @override
  Widget build(BuildContext context) {
    if (items.isEmpty) return const Text('No data.');
    final max = items
        .map((e) => e.value)
        .fold<double>(1, (a, b) => b > a ? b : a);
    return Column(
      children: [
        for (final item in items)
          Padding(
            padding: const EdgeInsets.symmetric(vertical: 3),
            child: Row(
              children: [
                SizedBox(
                  width: 120,
                  child: Text(
                    item.key,
                    overflow: TextOverflow.ellipsis,
                    style: Theme.of(context).textTheme.bodySmall,
                  ),
                ),
                Expanded(
                  child: Stack(
                    children: [
                      Container(
                        height: 18,
                        decoration: BoxDecoration(
                          color: color.withValues(alpha: 0.15),
                          borderRadius: BorderRadius.circular(4),
                        ),
                      ),
                      FractionallySizedBox(
                        widthFactor: (item.value / max).clamp(0.0, 1.0),
                        child: Container(
                          height: 18,
                          decoration: BoxDecoration(
                            color: color,
                            borderRadius: BorderRadius.circular(4),
                          ),
                        ),
                      ),
                    ],
                  ),
                ),
                const SizedBox(width: 8),
                Text(
                  item.value == item.value.roundToDouble()
                      ? item.value.toInt().toString()
                      : item.value.toStringAsFixed(1),
                  style: Theme.of(context).textTheme.bodySmall,
                ),
              ],
            ),
          ),
      ],
    );
  }
}

class _CaptureLog extends ConsumerWidget {
  const _CaptureLog({required this.query});

  final AdminAttendanceQuery query;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final events = ref.watch(adminPunchEventsProvider(query));
    return AsyncBody(
      value: events,
      onRetry: () => ref.invalidate(adminPunchEventsProvider(query)),
      isEmpty: (data) => (data['items'] as List? ?? const []).isEmpty,
      emptyTitle: 'No captured punches',
      emptyMessage: 'Location and selfie captures appear here after employees check in or out.',
      emptyIcon: Icons.photo_camera_outlined,
      builder: (data) {
        final items = (data['items'] as List? ?? const [])
            .whereType<Map>()
            .toList();
        return ListView.separated(
          padding: const EdgeInsets.all(12),
          itemCount: items.length,
          separatorBuilder: (_, _) => const SizedBox(height: 8),
          itemBuilder: (context, index) {
            final e = Map<String, dynamic>.from(items[index]);
            final hasPhoto = e['hasPhoto'] == true;
            final lat = e['latitude'];
            final lon = e['longitude'];
            return Card(
              child: ListTile(
                leading: CircleAvatar(
                  child: Icon(
                    e['direction'] == 'CheckIn'
                        ? Icons.login_rounded
                        : Icons.logout_rounded,
                  ),
                ),
                title: Text(
                  '${e['employeeName'] ?? ''} · ${e['direction'] ?? ''}',
                ),
                subtitle: Text(
                  [
                    '${e['employeeNumber'] ?? ''} · ${e['workDate'] ?? ''} ${e['localTime'] ?? ''}',
                    if (e['department'] != null) e['department'].toString(),
                    if (lat != null && lon != null)
                      'Location: $lat, $lon'
                    else
                      'Location: not captured',
                  ].join('\n'),
                ),
                isThreeLine: true,
                trailing: hasPhoto
                    ? IconButton(
                        tooltip: 'View selfie',
                        icon: const Icon(Icons.image_outlined),
                        onPressed: () => _viewPhoto(context, ref, e),
                      )
                    : const Icon(Icons.no_photography_outlined),
              ),
            );
          },
        );
      },
    );
  }

  Future<void> _viewPhoto(
    BuildContext context,
    WidgetRef ref,
    Map<String, dynamic> event,
  ) async {
    showDialog<void>(
      context: context,
      builder: (dialogContext) => Dialog(
        child: FutureBuilder<List<int>>(
          future: ref
              .read(apiClientProvider)
              .downloadBytes(
                '/admin/attendance/punch-events/${event['id']}/photo',
              ),
          builder: (context, snapshot) {
            if (snapshot.connectionState != ConnectionState.done) {
              return const Padding(
                padding: EdgeInsets.all(40),
                child: Center(child: CircularProgressIndicator()),
              );
            }
            if (snapshot.hasError || snapshot.data == null) {
              return const Padding(
                padding: EdgeInsets.all(24),
                child: Text('The selfie could not be loaded.'),
              );
            }
            return Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Padding(
                  padding: const EdgeInsets.all(12),
                  child: Text(
                    '${event['employeeName']} · ${event['workDate']} ${event['localTime']}',
                    style: Theme.of(context).textTheme.titleSmall,
                  ),
                ),
                Flexible(
                  child: InteractiveViewer(
                    child: Image.memory(
                      Uint8List.fromList(snapshot.data!),
                      fit: BoxFit.contain,
                    ),
                  ),
                ),
                TextButton(
                  onPressed: () => Navigator.of(dialogContext).pop(),
                  child: const Text(Strings.close),
                ),
              ],
            );
          },
        ),
      ),
    );
  }
}

class _CaptureSettingsTab extends ConsumerWidget {
  const _CaptureSettingsTab();

  Future<void> _setFlag(
    BuildContext context,
    WidgetRef ref,
    String key,
    bool value,
  ) async {
    try {
      await ref
          .read(adminUserActionsProvider)
          .updateConfiguration(key, value ? 'true' : 'false');
      ref.invalidate(adminConfigurationProvider);
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text(Strings.savedSuccessfully)),
        );
      }
    } catch (error) {
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Could not update setting: $error')),
        );
      }
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final config = ref.watch(adminConfigurationProvider);
    final canManage = ref
        .watch(authStateProvider)
        .hasPermission('admin.config');
    return AsyncBody(
      value: config,
      onRetry: () => ref.invalidate(adminConfigurationProvider),
      builder: (items) {
        bool flag(String key) {
          final entry = items.where((e) => e.key == key).firstOrNull;
          return entry == null || entry.value.toLowerCase() == 'true';
        }

        final location = flag('Attendance.CaptureLocation');
        final photo = flag('Attendance.CapturePhoto');
        return ListView(
          padding: const EdgeInsets.all(16),
          children: [
            Text(
              'Punch capture',
              style: Theme.of(context).textTheme.titleMedium,
            ),
            const SizedBox(height: 4),
            const Text(
              'When enabled, employees are prompted for a selfie and location '
              'on check-in/out. The captured data is visible only to '
              'administrators; employees never see it. Punches still succeed '
              'if an employee declines a permission.',
            ),
            const SizedBox(height: 12),
            Card(
              child: SwitchListTile(
                title: const Text('Capture location'),
                subtitle: const Text('Record GPS coordinates on each punch.'),
                value: location,
                onChanged: canManage
                    ? (v) => _setFlag(
                        context,
                        ref,
                        'Attendance.CaptureLocation',
                        v,
                      )
                    : null,
              ),
            ),
            Card(
              child: SwitchListTile(
                title: const Text('Capture selfie photo'),
                subtitle: const Text(
                  'Record a front-camera photo on each punch.',
                ),
                value: photo,
                onChanged: canManage
                    ? (v) =>
                          _setFlag(context, ref, 'Attendance.CapturePhoto', v)
                    : null,
              ),
            ),
            if (!canManage)
              const Padding(
                padding: EdgeInsets.only(top: 12),
                child: Text(
                  'You need the admin.config permission to change these settings.',
                ),
              ),
          ],
        );
      },
    );
  }
}
