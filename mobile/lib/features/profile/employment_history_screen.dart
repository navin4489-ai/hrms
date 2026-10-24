import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/strings.dart';
import '../../core/utils/formatters.dart';
import '../../providers/profile_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';
import '../../widgets/section_card.dart';

/// Employment history events (FR-EMP-025): transfers, promotions, designations.
class EmploymentHistoryScreen extends ConsumerWidget {
  const EmploymentHistoryScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final history = ref.watch(employmentHistoryProvider);

    return AppScaffold(
      title: Strings.employmentHistory,
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(employmentHistoryProvider),
        child: AsyncBody(
          value: history,
          onRetry: () => ref.invalidate(employmentHistoryProvider),
          isEmpty: (items) => items.isEmpty,
          emptyTitle: 'No history yet',
          emptyMessage: 'Changes to your employment record will appear here.',
          emptyIcon: Icons.history_edu_rounded,
          builder: (items) => ListView.separated(
            physics: const AlwaysScrollableScrollPhysics(),
            padding: const EdgeInsets.all(16),
            itemCount: items.length,
            separatorBuilder: (_, __) => const SizedBox(height: 10),
            itemBuilder: (context, index) {
              final entry = items[index];
              return SectionCard(
                title: AppFormatters.enumLabel(entry.eventType),
                dense: true,
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    InfoRow(
                      label: 'Effective',
                      value: AppFormatters.date(entry.effectiveDate),
                    ),
                    if (entry.oldValue != null || entry.newValue != null)
                      InfoRow(
                        label: 'Change',
                        value:
                            '${entry.oldValue ?? '-'} → ${entry.newValue ?? '-'}',
                      ),
                    if (entry.reason != null && entry.reason!.isNotEmpty)
                      InfoRow(label: Strings.reason, value: entry.reason!),
                    if (entry.approvedBy != null &&
                        entry.approvedBy!.isNotEmpty)
                      InfoRow(label: 'Approved by', value: entry.approvedBy!),
                  ],
                ),
              );
            },
          ),
        ),
      ),
    );
  }
}
