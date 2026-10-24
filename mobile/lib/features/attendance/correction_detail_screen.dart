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

/// One correction request with original/proposed values and the decision
/// history (FR-EMP-036).
class CorrectionDetailScreen extends ConsumerWidget {
  const CorrectionDetailScreen({super.key, required this.correctionId});

  final String correctionId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final detail = ref.watch(correctionDetailProvider(correctionId));

    return AppScaffold(
      title: Strings.correctionRequest,
      body: RefreshIndicator(
        onRefresh: () async =>
            ref.invalidate(correctionDetailProvider(correctionId)),
        child: AsyncBody(
          value: detail,
          onRetry: () => ref.invalidate(correctionDetailProvider(correctionId)),
          builder: (data) => SingleChildScrollView(
            physics: const AlwaysScrollableScrollPhysics(),
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Expanded(
                      child: Text(
                        AppFormatters.date(data.workDate),
                        style: Theme.of(context).textTheme.titleLarge
                            ?.copyWith(fontWeight: FontWeight.w700),
                      ),
                    ),
                    StatusChip(status: data.status),
                  ],
                ),
                const SizedBox(height: 4),
                Text(
                  '${AppFormatters.enumLabel(data.type)} · '
                  '${Strings.requestedAt}: ${AppFormatters.timestamp(data.submittedAt)}',
                  style: Theme.of(context).textTheme.bodySmall?.copyWith(
                    color: Theme.of(context).colorScheme.onSurfaceVariant,
                  ),
                ),
                const SizedBox(height: 16),
                SectionCard(
                  title: Strings.originalValues,
                  child: Column(
                    children: [
                      InfoRow(
                        label: Strings.checkInLabel,
                        value: AppFormatters.clockTime(data.originalCheckIn),
                      ),
                      InfoRow(
                        label: Strings.checkOutLabel,
                        value: AppFormatters.clockTime(data.originalCheckOut),
                      ),
                    ],
                  ),
                ),
                const SizedBox(height: 12),
                SectionCard(
                  title: 'Proposed values',
                  child: Column(
                    children: [
                      InfoRow(
                        label: Strings.checkInLabel,
                        value: AppFormatters.clockTime(data.proposedCheckIn),
                      ),
                      InfoRow(
                        label: Strings.checkOutLabel,
                        value: AppFormatters.clockTime(data.proposedCheckOut),
                      ),
                    ],
                  ),
                ),
                const SizedBox(height: 12),
                SectionCard(
                  title: Strings.summary,
                  child: Column(
                    children: [
                      InfoRow(
                        label: Strings.reasonLabel,
                        value: data.reason ?? '-',
                      ),
                      InfoRow(
                        label: Strings.manager,
                        value: data.approverName ?? '-',
                      ),
                      if (data.approvalTaskId != null)
                        InfoRow(
                          label: 'Approval task',
                          value: data.approvalTaskId!,
                        ),
                    ],
                  ),
                ),
                if (data.decisionHistory.isNotEmpty) ...[
                  const SizedBox(height: 12),
                  SectionCard(
                    title: Strings.decisionHistory,
                    child: Column(
                      children: [
                        for (final entry in data.decisionHistory)
                          _HistoryTile(entry: entry),
                      ],
                    ),
                  ),
                ],
                if (data.approvalTaskId != null) ...[
                  const SizedBox(height: 16),
                  SizedBox(
                    width: double.infinity,
                    child: OutlinedButton.icon(
                      onPressed: () =>
                          context.go('/approvals/${data.approvalTaskId}'),
                      icon: const Icon(Icons.how_to_reg_rounded, size: 18),
                      label: const Text('Open in approvals'),
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
}

class _HistoryTile extends StatelessWidget {
  const _HistoryTile({required this.entry});

  final DecisionEntry entry;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final action = entry.action;
    final actor = entry.actorName;
    final comment = entry.comment;
    final at = entry.at;
    final step = entry.step;
    return ListTile(
      contentPadding: EdgeInsets.zero,
      dense: true,
      leading: Icon(
        action == 'Approve'
            ? Icons.check_circle_outline_rounded
            : action == 'Reject'
            ? Icons.cancel_outlined
            : Icons.subdirectory_arrow_right_rounded,
        size: 20,
      ),
      title: Text(
        [
          if (action != null) AppFormatters.enumLabel(action),
          if (actor != null) actor,
        ].join(' · '),
      ),
      subtitle: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          if (step != null && step.isNotEmpty)
            Text(step, style: theme.textTheme.bodySmall),
          if (comment != null && comment.isNotEmpty) Text(comment),
          if (at != null)
            Text(
              AppFormatters.timestamp(at),
              style: theme.textTheme.bodySmall?.copyWith(
                color: theme.colorScheme.onSurfaceVariant,
              ),
            ),
        ],
      ),
    );
  }
}
