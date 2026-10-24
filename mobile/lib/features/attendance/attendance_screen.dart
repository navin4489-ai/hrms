import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/strings.dart';
import '../../core/utils/formatters.dart';
import '../../models/attendance_models.dart';
import '../../providers/attendance_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';
import '../../widgets/section_card.dart';
import '../../widgets/status_chip.dart';

/// Monthly attendance calendar (FR-EMP-030) with summary, legend, day detail
/// and entry points to correction requests (FR-EMP-035).
class AttendanceScreen extends ConsumerStatefulWidget {
  const AttendanceScreen({super.key});

  @override
  ConsumerState<AttendanceScreen> createState() => _AttendanceScreenState();
}

class _AttendanceScreenState extends ConsumerState<AttendanceScreen> {
  DateTime _month = DateTime(DateTime.now().year, DateTime.now().month);

  String get _monthKey =>
      '${_month.year}-${_month.month.toString().padLeft(2, '0')}';

  void _shiftMonth(int delta) {
    setState(() {
      _month = DateTime(_month.year, _month.month + delta);
    });
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final attendance = ref.watch(attendanceMonthProvider(_monthKey));

    return AppScaffold(
      title: Strings.attendanceTitle,
      actions: [
        IconButton(
          tooltip: Strings.myCorrections,
          icon: const Icon(Icons.fact_check_outlined),
          onPressed: () => context.go('/attendance/corrections'),
        ),
      ],
      body: RefreshIndicator(
        onRefresh: () async =>
            ref.invalidate(attendanceMonthProvider(_monthKey)),
        child: AsyncBody(
          value: attendance,
          onRetry: () => ref.invalidate(attendanceMonthProvider(_monthKey)),
          builder: (data) {
            final records = <String, AttendanceRecord>{
              for (final record in data.records) record.workDate: record,
            };
            return SingleChildScrollView(
              physics: const AlwaysScrollableScrollPhysics(),
              padding: const EdgeInsets.all(16),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  _MonthHeader(
                    month: _month,
                    onPrev:
                        _month.month == DateTime.now().month &&
                            _month.year == DateTime.now().year
                        ? null
                        : () => _shiftMonth(-1),
                    onNext: () => _shiftMonth(1),
                  ),
                  const SizedBox(height: 16),
                  _SummaryCard(summary: data.summary),
                  const SizedBox(height: 16),
                  SectionCard(
                    title: AppFormatters.monthYear(_month),
                    child: Column(
                      children: [
                        const _WeekdayHeader(),
                        const SizedBox(height: 8),
                        _MonthGrid(
                          month: _month,
                          records: records,
                          onDayTap: (record) => _showDayDetail(record),
                          onEmptyDayTap: (day) => _showEmptyDayDetail(day),
                        ),
                        const SizedBox(height: 14),
                        const _Legend(),
                      ],
                    ),
                  ),
                  const SizedBox(height: 16),
                  Row(
                    children: [
                      Expanded(
                        child: FilledButton.tonalIcon(
                          onPressed: () =>
                              context.go('/attendance/corrections/new'),
                          icon: const Icon(
                            Icons.edit_calendar_rounded,
                            size: 18,
                          ),
                          label: const Text(Strings.newCorrection),
                        ),
                      ),
                      const SizedBox(width: 12),
                      Expanded(
                        child: OutlinedButton.icon(
                          onPressed: () =>
                              context.go('/attendance/corrections'),
                          icon: const Icon(Icons.fact_check_outlined, size: 18),
                          label: const Text(Strings.myCorrections),
                        ),
                      ),
                    ],
                  ),
                ],
              ),
            );
          },
        ),
      ),
    );
  }

  void _showDayDetail(AttendanceRecord record) {
    final theme = Theme.of(context);
    showModalBottomSheet<void>(
      context: context,
      showDragHandle: true,
      builder: (sheetContext) => Padding(
        padding: const EdgeInsets.fromLTRB(20, 4, 20, 28),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(
                    AppFormatters.date(record.workDate),
                    style: theme.textTheme.titleLarge?.copyWith(
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                ),
                StatusChip(status: record.status),
              ],
            ),
            const SizedBox(height: 16),
            InfoRow(
              label: Strings.checkInLabel,
              value: AppFormatters.clockTime(record.checkIn),
            ),
            InfoRow(
              label: Strings.checkOutLabel,
              value: AppFormatters.clockTime(record.checkOut),
            ),
            InfoRow(
              label: Strings.workedHours,
              value: AppFormatters.hours(record.workedHours),
            ),
            if (record.shiftName != null && record.shiftName!.isNotEmpty)
              InfoRow(label: Strings.shiftLabel, value: record.shiftName!),
            if (record.lateByMinutes != null && record.lateByMinutes! > 0)
              InfoRow(
                label: 'Late',
                value: '${record.lateByMinutes} min',
                valueStyle: TextStyle(color: theme.colorScheme.error),
              ),
            if (record.correctionStatus != null)
              InfoRow(
                label: Strings.correctionRequest,
                value: AppFormatters.enumLabel(record.correctionStatus),
              ),
            const SizedBox(height: 16),
            SizedBox(
              width: double.infinity,
              child: FilledButton.icon(
                onPressed: () {
                  Navigator.of(sheetContext).pop();
                  context.go('/attendance/corrections/new');
                },
                icon: const Icon(Icons.edit_calendar_rounded, size: 18),
                label: const Text(Strings.newCorrection),
              ),
            ),
          ],
        ),
      ),
    );
  }

  void _showEmptyDayDetail(DateTime day) {
    final theme = Theme.of(context);
    showModalBottomSheet<void>(
      context: context,
      showDragHandle: true,
      builder: (sheetContext) => Padding(
        padding: const EdgeInsets.fromLTRB(20, 4, 20, 28),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              AppFormatters.formatDate(day),
              style: theme.textTheme.titleLarge?.copyWith(
                fontWeight: FontWeight.w700,
              ),
            ),
            const SizedBox(height: 12),
            Text(
              'No attendance record for this day. If you worked or forgot to '
              'punch, request a correction.',
              style: theme.textTheme.bodyMedium?.copyWith(
                color: theme.colorScheme.onSurfaceVariant,
              ),
            ),
            const SizedBox(height: 16),
            SizedBox(
              width: double.infinity,
              child: FilledButton.icon(
                onPressed: () {
                  Navigator.of(sheetContext).pop();
                  context.go('/attendance/corrections/new');
                },
                icon: const Icon(Icons.edit_calendar_rounded, size: 18),
                label: const Text(Strings.newCorrection),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _MonthHeader extends StatelessWidget {
  const _MonthHeader({
    required this.month,
    required this.onPrev,
    required this.onNext,
  });

  final DateTime month;
  final VoidCallback? onPrev;
  final VoidCallback onNext;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Row(
      children: [
        IconButton(
          tooltip: 'Previous month',
          onPressed: onPrev,
          icon: const Icon(Icons.chevron_left_rounded),
        ),
        Expanded(
          child: Text(
            AppFormatters.monthYear(month),
            textAlign: TextAlign.center,
            style: theme.textTheme.titleMedium?.copyWith(
              fontWeight: FontWeight.w700,
            ),
          ),
        ),
        IconButton(
          tooltip: 'Next month',
          onPressed: onNext,
          icon: const Icon(Icons.chevron_right_rounded),
        ),
      ],
    );
  }
}

class _SummaryCard extends StatelessWidget {
  const _SummaryCard({required this.summary});

  final AttendanceMonthSummary summary;

  @override
  Widget build(BuildContext context) {
    final items = <(String, int, IconData)>[
      ('Present', summary.present, Icons.work_rounded),
      ('Absent', summary.absent, Icons.free_cancellation_rounded),
      ('Leave', summary.leave, Icons.beach_access_rounded),
      ('Half day', summary.halfDay, Icons.adjust_rounded),
      ('Holidays', summary.holidays, Icons.celebration_rounded),
      ('Week offs', summary.weekOffs, Icons.weekend_rounded),
      ('Late', summary.late, Icons.near_me_rounded),
      (
        'Pending corrections',
        summary.pendingCorrections,
        Icons.fact_check_outlined,
      ),
    ];
    return SectionCard(
      title: Strings.monthSummary,
      child: Wrap(
        spacing: 12,
        runSpacing: 12,
        children: [
          for (final (label, value, icon) in items)
            SizedBox(
              width: 150,
              child: _SummaryTile(label: label, value: value, icon: icon),
            ),
        ],
      ),
    );
  }
}

class _SummaryTile extends StatelessWidget {
  const _SummaryTile({
    required this.label,
    required this.value,
    required this.icon,
  });

  final String label;
  final int value;
  final IconData icon;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    return Container(
      padding: const EdgeInsets.all(10),
      decoration: BoxDecoration(
        color: scheme.surfaceContainerHighest.withValues(alpha: 0.55),
        borderRadius: BorderRadius.circular(12),
      ),
      child: Row(
        children: [
          Icon(icon, size: 18, color: scheme.primary),
          const SizedBox(width: 8),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  '$value',
                  style: theme.textTheme.titleMedium?.copyWith(
                    fontWeight: FontWeight.w800,
                  ),
                ),
                Text(
                  label,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: theme.textTheme.bodySmall?.copyWith(
                    color: scheme.onSurfaceVariant,
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _WeekdayHeader extends StatelessWidget {
  const _WeekdayHeader();

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Row(
      children: [
        for (final day in const [
          'Mon',
          'Tue',
          'Wed',
          'Thu',
          'Fri',
          'Sat',
          'Sun',
        ])
          Expanded(
            child: Center(
              child: Text(
                day,
                style: theme.textTheme.bodySmall?.copyWith(
                  fontWeight: FontWeight.w700,
                ),
              ),
            ),
          ),
      ],
    );
  }
}

class _MonthGrid extends StatelessWidget {
  const _MonthGrid({
    required this.month,
    required this.records,
    required this.onDayTap,
    required this.onEmptyDayTap,
  });

  final DateTime month;
  final Map<String, AttendanceRecord> records;
  final ValueChanged<AttendanceRecord> onDayTap;
  final ValueChanged<DateTime> onEmptyDayTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final firstDay = DateTime(month.year, month.month, 1);
    final daysInMonth = DateTime(month.year, month.month + 1, 0).day;
    // Monday-first offset.
    final leading = (firstDay.weekday + 6) % 7;
    final cells = <Widget>[];
    for (var i = 0; i < leading; i++) {
      cells.add(const SizedBox.shrink());
    }
    for (var day = 1; day <= daysInMonth; day++) {
      final date = DateTime(month.year, month.month, day);
      final iso =
          '${month.year}-${month.month.toString().padLeft(2, '0')}-${day.toString().padLeft(2, '0')}';
      final record = records[iso];
      final isToday = AppFormatters.isToday(iso);
      cells.add(
        _DayCell(
          day: day,
          record: record,
          isToday: isToday,
          onTap: record != null
              ? () => onDayTap(record)
              : () => onEmptyDayTap(date),
          theme: theme,
        ),
      );
    }
    return GridView.count(
      crossAxisCount: 7,
      shrinkWrap: true,
      physics: const NeverScrollableScrollPhysics(),
      mainAxisSpacing: 6,
      crossAxisSpacing: 6,
      childAspectRatio: 0.78,
      children: cells,
    );
  }
}

class _DayCell extends StatelessWidget {
  const _DayCell({
    required this.day,
    required this.record,
    required this.isToday,
    required this.onTap,
    required this.theme,
  });

  final int day;
  final AttendanceRecord? record;
  final bool isToday;
  final VoidCallback onTap;
  final ThemeData theme;

  @override
  Widget build(BuildContext context) {
    final scheme = theme.colorScheme;
    final status = record?.status;
    final visual = status == null ? null : statusVisual(status);
    final color = visual?.color;
    return Semantics(
      button: true,
      label:
          'Day $day${status == null ? '' : ', ${AppFormatters.enumLabel(status)}'}',
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(10),
        child: Container(
          decoration: BoxDecoration(
            color: color == null
                ? scheme.surfaceContainerHighest.withValues(alpha: 0.4)
                : color.withValues(alpha: 0.14),
            borderRadius: BorderRadius.circular(10),
            border: isToday
                ? Border.all(color: scheme.primary, width: 2)
                : Border.all(color: scheme.outlineVariant),
          ),
          padding: const EdgeInsets.symmetric(vertical: 4, horizontal: 2),
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              Text(
                '$day',
                style: theme.textTheme.bodySmall?.copyWith(
                  fontWeight: isToday ? FontWeight.w800 : FontWeight.w600,
                ),
              ),
              if (visual != null) ...[
                const SizedBox(height: 2),
                Icon(visual.icon, size: 14, color: visual.color),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

class _Legend extends StatelessWidget {
  const _Legend();

  @override
  Widget build(BuildContext context) {
    final entries = <String>[
      'Present',
      'Absent',
      'Leave',
      'Holiday',
      'WeekOff',
      'HalfDay',
      'MissingPunch',
      'NotMarked',
    ];
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          Strings.legend,
          style: Theme.of(context).textTheme.bodySmall
              ?.copyWith(fontWeight: FontWeight.w700),
        ),
        const SizedBox(height: 8),
        Wrap(
          spacing: 8,
          runSpacing: 8,
          children: [
            for (final status in entries)
              StatusChip(status: status, dense: true),
          ],
        ),
      ],
    );
  }
}
