import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/strings.dart';
import '../../core/utils/formatters.dart';
import '../../models/approvals_models.dart';
import '../../providers/approvals_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';
import '../../widgets/status_chip.dart';

/// Approvals inbox (FR-MGR-001): pending tasks awaiting my decision plus my
/// own requests' workflow state.
class ApprovalsScreen extends ConsumerStatefulWidget {
  const ApprovalsScreen({super.key});

  @override
  ConsumerState<ApprovalsScreen> createState() => _ApprovalsScreenState();
}

class _ApprovalsScreenState extends ConsumerState<ApprovalsScreen> {
  int _page = 1;

  @override
  Widget build(BuildContext context) {
    return DefaultTabController(
      length: 2,
      child: AppScaffold(
        title: Strings.approvalsTitle,
        bottom: const TabBar(
          tabs: [
            Tab(text: Strings.pending),
            Tab(text: Strings.myWorkflow),
          ],
        ),
        body: TabBarView(
          children: [
            // ------------------------------------------------------ pending
            RefreshIndicator(
              onRefresh: () async {
                setState(() => _page = 1);
                ref.invalidate(pendingApprovalsProvider);
                ref.invalidate(approvalStatsProvider);
              },
              child: AsyncBody(
                value: ref.watch(pendingApprovalsProvider(1)),
                onRetry: () => ref.invalidate(pendingApprovalsProvider),
                isEmpty: (page) => page.items.isEmpty && _page == 1,
                emptyTitle: Strings.noPendingApprovals,
                emptyMessage: Strings.noPendingApprovalsHint,
                emptyIcon: Icons.task_alt_rounded,
                builder: (firstPage) {
                  final extraPages = [
                    for (var p = 2; p <= _page; p++)
                      ref.watch(pendingApprovalsProvider(p)).value,
                  ];
                  final items = <ApprovalTask>[
                    ...firstPage.items,
                    for (final page in extraPages)
                      if (page != null) ...page.items,
                  ];
                  final lastHasMore =
                      (extraPages.isNotEmpty
                          ? extraPages.last?.hasNextPage
                          : firstPage.hasNextPage) ??
                      false;
                  return ListView(
                    physics: const AlwaysScrollableScrollPhysics(),
                    padding: const EdgeInsets.fromLTRB(16, 8, 16, 16),
                    children: [
                      const _StatsRow(),
                      const SizedBox(height: 8),
                      for (final task in items) ...[
                        _TaskTile(task: task),
                        const SizedBox(height: 10),
                      ],
                      if (lastHasMore)
                        Center(
                          child: TextButton.icon(
                            onPressed: () => setState(() => _page += 1),
                            icon: const Icon(Icons.expand_more_rounded),
                            label: const Text(Strings.loadMore),
                          ),
                        ),
                    ],
                  );
                },
              ),
            ),
            // ------------------------------------------------------- mine
            RefreshIndicator(
              onRefresh: () async => ref.invalidate(myApprovalsProvider),
              child: AsyncBody(
                value: ref.watch(myApprovalsProvider),
                onRetry: () => ref.invalidate(myApprovalsProvider),
                isEmpty: (items) => items.isEmpty,
                emptyTitle: 'No requests',
                emptyMessage:
                    'Your own requests and their workflow state '
                    'will appear here.',
                builder: (items) => ListView.separated(
                  physics: const AlwaysScrollableScrollPhysics(),
                  padding: const EdgeInsets.fromLTRB(16, 16, 16, 16),
                  itemCount: items.length,
                  separatorBuilder: (_, __) => const SizedBox(height: 10),
                  itemBuilder: (context, index) {
                    final item = items[index];
                    return Card(
                      clipBehavior: Clip.antiAlias,
                      child: ListTile(
                        title: Text(item.title),
                        subtitle: Text(
                          [
                            AppFormatters.enumLabel(item.requestType),
                            if (item.currentStep != null &&
                                item.currentStep!.isNotEmpty)
                              item.currentStep!,
                            if (item.approverName != null)
                              'with ${item.approverName}',
                          ].join(' · '),
                        ),
                        trailing: StatusChip(status: item.status, dense: true),
                        onTap: () => context.go('/approvals/${item.taskId}'),
                      ),
                    );
                  },
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _StatsRow extends ConsumerWidget {
  const _StatsRow();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final stats = ref.watch(approvalStatsProvider).value;
    final scheme = Theme.of(context).colorScheme;
    return Row(
      children: [
        Expanded(
          child: _StatTile(
            label: Strings.pending,
            value: '${stats?.pending ?? 0}',
            color: scheme.primary,
          ),
        ),
        const SizedBox(width: 10),
        Expanded(
          child: _StatTile(
            label: Strings.overdueLabel,
            value: '${stats?.overdue ?? 0}',
            color: scheme.error,
          ),
        ),
        const SizedBox(width: 10),
        Expanded(
          child: _StatTile(
            label: Strings.completedThisMonth,
            value: '${stats?.completedThisMonth ?? 0}',
            color: scheme.tertiary,
          ),
        ),
      ],
    );
  }
}

class _StatTile extends StatelessWidget {
  const _StatTile({
    required this.label,
    required this.value,
    required this.color,
  });

  final String label;
  final String value;
  final Color color;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Container(
      padding: const EdgeInsets.symmetric(vertical: 10, horizontal: 8),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.12),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: color.withValues(alpha: 0.4)),
      ),
      child: Column(
        children: [
          Text(
            value,
            style: theme.textTheme.titleLarge?.copyWith(
              fontWeight: FontWeight.w800,
              color: color,
            ),
          ),
          Text(
            label,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: theme.textTheme.bodySmall,
          ),
        ],
      ),
    );
  }
}

class _TaskTile extends StatelessWidget {
  const _TaskTile({required this.task});

  final ApprovalTask task;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    return Card(
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: () => context.go('/approvals/${task.taskId}'),
        child: Padding(
          padding: const EdgeInsets.all(12),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Expanded(
                    child: Text(
                      task.title,
                      style: theme.textTheme.titleSmall?.copyWith(
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                  ),
                  StatusChip(status: task.status, dense: true),
                ],
              ),
              const SizedBox(height: 6),
              Text(
                [
                  AppFormatters.enumLabel(task.requestType),
                  if (task.requesterName != null) task.requesterName!,
                ].join(' · '),
                style: theme.textTheme.bodySmall?.copyWith(
                  color: scheme.onSurfaceVariant,
                ),
              ),
              const SizedBox(height: 8),
              Row(
                children: [
                  if (task.stepName != null && task.stepName!.isNotEmpty) ...[
                    const Icon(Icons.route_rounded, size: 15),
                    const SizedBox(width: 4),
                    Text(task.stepName!, style: theme.textTheme.bodySmall),
                    const SizedBox(width: 12),
                  ],
                  if (task.dueAt != null) ...[
                    Icon(
                      task.isOverdue
                          ? Icons.warning_amber_rounded
                          : Icons.schedule_rounded,
                      size: 15,
                      color: task.isOverdue ? scheme.error : null,
                    ),
                    const SizedBox(width: 4),
                    Text(
                      task.isOverdue
                          ? Strings.overdueLabel
                          : AppFormatters.relativeTime(task.dueAt),
                      style: theme.textTheme.bodySmall?.copyWith(
                        color: task.isOverdue ? scheme.error : null,
                      ),
                    ),
                  ],
                  const Spacer(),
                  if (task.amount != null)
                    Text(
                      AppFormatters.currency(task.amount, task.currency),
                      style: theme.textTheme.bodySmall?.copyWith(
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }
}
