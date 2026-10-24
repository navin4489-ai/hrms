import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/strings.dart';
import '../../core/utils/formatters.dart';
import '../../models/leave_models.dart';
import '../../providers/leave_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';
import '../../widgets/section_card.dart';
import '../../widgets/status_chip.dart';

/// Team leave calendar for a month (FR-EMP-045): Leave / Holiday / WeekOff
/// days with labels.
class LeaveCalendarScreen extends ConsumerStatefulWidget {
  const LeaveCalendarScreen({super.key});

  @override
  ConsumerState<LeaveCalendarScreen> createState() =>
      _LeaveCalendarScreenState();
}

class _LeaveCalendarScreenState extends ConsumerState<LeaveCalendarScreen> {
  DateTime _month = DateTime(DateTime.now().year, DateTime.now().month);

  String get _range {
    final from = DateTime(_month.year, _month.month, 1);
    final to = DateTime(_month.year, _month.month + 1, 0);
    return '${AppFormatters.toIsoDate(from)}|${AppFormatters.toIsoDate(to)}';
  }

  void _shiftMonth(int delta) {
    setState(() => _month = DateTime(_month.year, _month.month + delta));
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final entries = ref.watch(leaveCalendarProvider(_range));

    return AppScaffold(
      title: Strings.leaveCalendar,
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(leaveCalendarProvider(_range)),
        child: AsyncBody(
          value: entries,
          onRetry: () => ref.invalidate(leaveCalendarProvider(_range)),
          builder: (items) {
            final byDate = <String, LeaveCalendarEntry>{
              for (final entry in items) entry.date: entry,
            };
            return SingleChildScrollView(
              physics: const AlwaysScrollableScrollPhysics(),
              padding: const EdgeInsets.all(16),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      IconButton(
                        tooltip: 'Previous month',
                        onPressed: () => _shiftMonth(-1),
                        icon: const Icon(Icons.chevron_left_rounded),
                      ),
                      Expanded(
                        child: Text(
                          AppFormatters.monthYear(_month),
                          textAlign: TextAlign.center,
                          style: theme.textTheme.titleMedium?.copyWith(
                            fontWeight: FontWeight.w700,
                          ),
                        ),
                      ),
                      IconButton(
                        tooltip: 'Next month',
                        onPressed: () => _shiftMonth(1),
                        icon: const Icon(Icons.chevron_right_rounded),
                      ),
                    ],
                  ),
                  const SizedBox(height: 8),
                  SectionCard(
                    title: AppFormatters.monthYear(_month),
                    child: Column(
                      children: [
                        const _WeekdayHeader(),
                        const SizedBox(height: 8),
                        _Grid(
                          month: _month,
                          byDate: byDate,
                          onDayTap: _showDetail,
                        ),
                        const SizedBox(height: 14),
                        Wrap(
                          spacing: 8,
                          runSpacing: 8,
                          children: const [
                            StatusChip(status: 'Leave', dense: true),
                            StatusChip(status: 'Holiday', dense: true),
                            StatusChip(status: 'WeekOff', dense: true),
                          ],
                        ),
                      ],
                    ),
                  ),
                  if (items.isNotEmpty) ...[
                    const SizedBox(height: 12),
                    SectionCard(
                      title: 'Entries',
                      child: Column(
                        children: [
                          for (final entry in items)
                            ListTile(
                              contentPadding: EdgeInsets.zero,
                              dense: true,
                              leading: const Icon(Icons.event_rounded),
                              title: Text(AppFormatters.date(entry.date)),
                              subtitle: Text(
                                [
                                  if (entry.label != null &&
                                      entry.label!.isNotEmpty)
                                    entry.label!,
                                  AppFormatters.enumLabel(entry.status),
                                ].join(' · '),
                              ),
                              trailing: StatusChip(
                                status: entry.type,
                                dense: true,
                              ),
                            ),
                        ],
                      ),
                    ),
                  ],
                ],
              ),
            );
          },
        ),
      ),
    );
  }

  void _showDetail(DateTime day) {
    final theme = Theme.of(context);
    final iso = AppFormatters.toIsoDate(day);
    final items = ref.read(leaveCalendarProvider(_range)).value ?? [];
    LeaveCalendarEntry? entry;
    for (final item in items) {
      if (item.date == iso) {
        entry = item;
        break;
      }
    }

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
            if (entry == null)
              Text(
                'No leave, holiday or week off on this day.',
                style: theme.textTheme.bodyMedium?.copyWith(
                  color: theme.colorScheme.onSurfaceVariant,
                ),
              )
            else ...[
              Row(
                children: [
                  StatusChip(status: entry.type),
                  const SizedBox(width: 8),
                  if (entry.status != null)
                    StatusChip(status: entry.status, dense: true),
                ],
              ),
              const SizedBox(height: 10),
              if (entry.label != null && entry.label!.isNotEmpty)
                Text(entry.label!, style: theme.textTheme.bodyMedium),
            ],
            const SizedBox(height: 16),
            SizedBox(
              width: double.infinity,
              child: OutlinedButton(
                onPressed: () => Navigator.of(sheetContext).pop(),
                child: const Text(Strings.close),
              ),
            ),
          ],
        ),
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

class _Grid extends StatelessWidget {
  const _Grid({
    required this.month,
    required this.byDate,
    required this.onDayTap,
  });

  final DateTime month;
  final Map<String, LeaveCalendarEntry> byDate;
  final ValueChanged<DateTime> onDayTap;

  static Color _colorFor(String type, ColorScheme scheme) =>
      statusVisual(type, scheme: scheme).color;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final firstDay = DateTime(month.year, month.month, 1);
    final daysInMonth = DateTime(month.year, month.month + 1, 0).day;
    final leading = (firstDay.weekday + 6) % 7;
    final cells = <Widget>[
      for (var i = 0; i < leading; i++) const SizedBox.shrink(),
    ];
    for (var day = 1; day <= daysInMonth; day++) {
      final date = DateTime(month.year, month.month, day);
      final iso = AppFormatters.toIsoDate(date);
      final entry = byDate[iso];
      final isToday = AppFormatters.isToday(iso);
      cells.add(
        Semantics(
          button: true,
          label:
              'Day $day${entry == null ? '' : ', ${AppFormatters.enumLabel(entry.type)}'}',
          child: InkWell(
            onTap: () => onDayTap(date),
            borderRadius: BorderRadius.circular(10),
            child: Container(
              decoration: BoxDecoration(
                color: entry == null
                    ? scheme.surfaceContainerHighest.withValues(alpha: 0.4)
                    : _colorFor(entry.type, scheme).withValues(alpha: 0.16),
                borderRadius: BorderRadius.circular(10),
                border: isToday
                    ? Border.all(color: scheme.primary, width: 2)
                    : Border.all(color: scheme.outlineVariant),
              ),
              padding: const EdgeInsets.symmetric(vertical: 4),
              child: Column(
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  Text(
                    '$day',
                    style: theme.textTheme.bodySmall?.copyWith(
                      fontWeight: isToday ? FontWeight.w800 : FontWeight.w600,
                    ),
                  ),
                  if (entry != null) ...[
                    const SizedBox(height: 2),
                    Container(
                      width: 8,
                      height: 8,
                      decoration: BoxDecoration(
                        color: _colorFor(entry.type, scheme),
                        shape: BoxShape.circle,
                      ),
                    ),
                  ],
                ],
              ),
            ),
          ),
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
