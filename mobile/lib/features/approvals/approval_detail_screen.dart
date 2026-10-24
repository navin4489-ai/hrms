import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/network/api_exception.dart';
import '../../core/strings.dart';
import '../../core/utils/formatters.dart';
import '../../models/approvals_models.dart';
import '../../models/attendance_models.dart';
import '../../providers/approvals_provider.dart';
import '../../providers/dashboard_provider.dart';
import '../../widgets/app_button.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';
import '../../widgets/section_card.dart';
import '../../widgets/status_chip.dart';

/// Approval task detail with the decision actions (FR-MGR-002).
/// Reject/Return require a non-empty comment (BR-007); the server enforces
/// the same rule with 422 as a fallback.
class ApprovalDetailScreen extends ConsumerStatefulWidget {
  const ApprovalDetailScreen({super.key, required this.taskId});

  final String taskId;

  @override
  ConsumerState<ApprovalDetailScreen> createState() =>
      _ApprovalDetailScreenState();
}

class _ApprovalDetailScreenState extends ConsumerState<ApprovalDetailScreen> {
  final _commentController = TextEditingController();
  bool _busy = false;

  @override
  void dispose() {
    _commentController.dispose();
    super.dispose();
  }

  Future<void> _decide(String action) async {
    final comment = _commentController.text.trim();
    final needsComment = action == 'Reject' || action == 'Return';
    if (needsComment && comment.isEmpty) {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            action == 'Reject'
                ? Strings.rejectCommentRequired
                : Strings.returnCommentRequired,
          ),
        ),
      );
      return;
    }

    FocusScope.of(context).unfocus();
    setState(() => _busy = true);
    try {
      final result = await ref
          .read(approvalActionsProvider)
          .decide(
            taskId: widget.taskId,
            action: action,
            comment: comment.isEmpty ? null : comment,
          );
      ref.invalidate(approvalDetailProvider(widget.taskId));
      ref.invalidate(pendingApprovalsProvider);
      ref.invalidate(approvalStatsProvider);
      ref.invalidate(myApprovalsProvider);
      ref.invalidate(dashboardProvider);
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            (result.message?.isNotEmpty ?? false)
                ? result.message!
                : '${Strings.decisionDone}: ${AppFormatters.enumLabel(result.status)}',
          ),
        ),
      );
      if (context.canPop()) {
        context.pop();
      } else {
        context.go('/approvals');
      }
    } on ApiException catch (e) {
      if (!mounted) return;
      final message = switch (e.statusCode) {
        403 => e.detail ?? Strings.notYourTask,
        409 => e.detail ?? Strings.alreadyDecided,
        422 => e.detail ?? e.firstFieldError ?? e.message,
        _ => e.detail ?? e.message,
      };
      ScaffoldMessenger.of(context)
          .showSnackBar(SnackBar(content: Text(message)));
      ref.invalidate(approvalDetailProvider(widget.taskId));
    } catch (_) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(const SnackBar(content: Text(Strings.offlineMessage)));
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final detail = ref.watch(approvalDetailProvider(widget.taskId));
    final theme = Theme.of(context);

    return AppScaffold(
      title: Strings.approvalsTitle,
      body: RefreshIndicator(
        onRefresh: () async =>
            ref.invalidate(approvalDetailProvider(widget.taskId)),
        child: AsyncBody(
          value: detail,
          onRetry: () => ref.invalidate(approvalDetailProvider(widget.taskId)),
          builder: (task) {
            final canDecide = task.canDecide && task.status == 'Pending';
            return SingleChildScrollView(
              physics: const AlwaysScrollableScrollPhysics(),
              padding: const EdgeInsets.all(16),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Expanded(
                        child: Text(
                          task.title,
                          style: theme.textTheme.titleLarge?.copyWith(
                            fontWeight: FontWeight.w700,
                          ),
                        ),
                      ),
                      StatusChip(status: task.status),
                    ],
                  ),
                  const SizedBox(height: 16),
                  SectionCard(
                    title: Strings.requester,
                    child: Column(
                      children: [
                        InfoRow(
                          label: 'Name',
                          value: task.requesterName ?? '-',
                        ),
                        InfoRow(
                          label: 'Type',
                          value: AppFormatters.enumLabel(task.requestType),
                        ),
                        if (task.currentStep != null &&
                            task.currentStep!.isNotEmpty)
                          InfoRow(
                            label: Strings.stepLabel,
                            value: task.currentStep!,
                          ),
                        InfoRow(
                          label: Strings.dueBy,
                          value: task.dueAt == null
                              ? '-'
                              : '${AppFormatters.timestamp(task.dueAt)}'
                                    '${task.dueAt != null && _isOverdue(task.dueAt) ? ' (${Strings.overdueLabel})' : ''}',
                        ),
                        InfoRow(label: 'Reference', value: task.requestId),
                      ],
                    ),
                  ),
                  if (task.history.isNotEmpty) ...[
                    const SizedBox(height: 12),
                    SectionCard(
                      title: Strings.decisionHistory,
                      child: Column(
                        children: [
                          for (final entry in task.history)
                            _HistoryRow(entry: entry),
                        ],
                      ),
                    ),
                  ],
                  const SizedBox(height: 16),
                  if (!canDecide)
                    Container(
                      padding: const EdgeInsets.all(12),
                      decoration: BoxDecoration(
                        color: theme.colorScheme.surfaceContainerHighest
                            .withValues(alpha: 0.6),
                        borderRadius: BorderRadius.circular(12),
                      ),
                      child: Text(
                        task.status == 'Pending'
                            ? Strings.notYourTask
                            : Strings.alreadyDecided,
                        style: theme.textTheme.bodyMedium,
                        textAlign: TextAlign.center,
                      ),
                    )
                  else ...[
                    TextField(
                      controller: _commentController,
                      enabled: !_busy,
                      maxLines: 3,
                      maxLength: 500,
                      decoration: const InputDecoration(
                        labelText: Strings.commentLabel,
                        hintText:
                            'Optional for approve; required for '
                            'reject / return',
                      ),
                      onChanged: (_) => setState(() {}),
                    ),
                    const SizedBox(height: 16),
                    AppButton(
                      label: Strings.approve,
                      icon: Icons.check_rounded,
                      busy: _busy,
                      onPressed: _busy ? null : () => _decide('Approve'),
                    ),
                    const SizedBox(height: 10),
                    Row(
                      children: [
                        Expanded(
                          child: OutlinedButton.icon(
                            onPressed: _busy ? null : () => _decide('Return'),
                            icon: const Icon(
                              Icons.assignment_returned_rounded,
                              size: 18,
                            ),
                            label: const Text(Strings.returnRequest),
                          ),
                        ),
                        const SizedBox(width: 12),
                        Expanded(
                          child: OutlinedButton.icon(
                            onPressed: _busy ? null : () => _decide('Reject'),
                            icon: const Icon(Icons.cancel_outlined, size: 18),
                            label: const Text(Strings.reject),
                            style: OutlinedButton.styleFrom(
                              foregroundColor: theme.colorScheme.error,
                              side: BorderSide(color: theme.colorScheme.error),
                            ),
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 8),
                    Text(
                      '${Strings.rejectCommentRequired}; '
                      '${Strings.returnCommentRequired}.',
                      style: theme.textTheme.bodySmall?.copyWith(
                        color: theme.colorScheme.onSurfaceVariant,
                      ),
                      textAlign: TextAlign.center,
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

  bool _isOverdue(String? dueAt) {
    final due = dueAt == null ? null : DateTime.tryParse(dueAt);
    return due != null && due.toUtc().isBefore(DateTime.now().toUtc());
  }
}

class _HistoryRow extends StatelessWidget {
  const _HistoryRow({required this.entry});

  final DecisionEntry entry;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final action = entry.action;
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
          if (entry.step != null && entry.step!.isNotEmpty) '${entry.step}: ',
          if (action != null) AppFormatters.enumLabel(action),
          if (entry.actorName != null) '· ${entry.actorName}',
        ].join(''),
      ),
      subtitle: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          if (entry.comment != null && entry.comment!.isNotEmpty)
            Text(entry.comment!),
          if (entry.at != null)
            Text(
              AppFormatters.timestamp(entry.at),
              style: theme.textTheme.bodySmall?.copyWith(
                color: theme.colorScheme.onSurfaceVariant,
              ),
            ),
        ],
      ),
    );
  }
}
