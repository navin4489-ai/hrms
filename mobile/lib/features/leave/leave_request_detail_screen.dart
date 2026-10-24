import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/network/api_exception.dart';
import '../../core/strings.dart';
import '../../core/utils/formatters.dart';
import '../../models/attendance_models.dart';
import '../../providers/leave_provider.dart';
import '../../widgets/app_button.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';
import '../../widgets/confirm_dialog.dart';
import '../../widgets/section_card.dart';
import '../../widgets/status_chip.dart';

/// Leave request detail with decision history and cancellation
/// (FR-EMP-043/044, reason is mandatory on cancel).
class LeaveRequestDetailScreen extends ConsumerStatefulWidget {
  const LeaveRequestDetailScreen({super.key, required this.requestId});

  final String requestId;

  @override
  ConsumerState<LeaveRequestDetailScreen> createState() =>
      _LeaveRequestDetailScreenState();
}

class _LeaveRequestDetailScreenState
    extends ConsumerState<LeaveRequestDetailScreen> {
  bool _busy = false;

  Future<String?> _promptReason() async {
    final controller = TextEditingController();
    final result = await showDialog<String>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text(Strings.reasonLabel),
        content: TextField(
          controller: controller,
          maxLines: 3,
          autofocus: true,
          decoration: const InputDecoration(
            hintText: 'Why are you cancelling this request?',
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(dialogContext).pop(),
            child: const Text(Strings.cancel),
          ),
          FilledButton(
            onPressed: () =>
                Navigator.of(dialogContext).pop(controller.text.trim()),
            child: const Text(Strings.confirm),
          ),
        ],
      ),
    );
    controller.dispose();
    return (result == null || result.isEmpty) ? null : result;
  }

  Future<void> _cancel() async {
    final confirmed = await showConfirmDialog(
      context,
      title: Strings.cancelRequest,
      message: Strings.cancelRequestConfirm,
      confirmLabel: Strings.cancelRequest,
      destructive: true,
    );
    if (!confirmed || !mounted) return;
    final reason = await _promptReason();
    if (reason == null || !mounted) return;

    setState(() => _busy = true);
    try {
      await ref
          .read(leaveActionsProvider)
          .cancelRequest(id: widget.requestId, reason: reason);
      ref.invalidate(leaveRequestDetailProvider(widget.requestId));
      ref.invalidate(leaveRequestsProvider);
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Leave request cancelled')),
        );
      }
    } on ApiException catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text(e.detail ?? e.message)));
      }
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
    final detail = ref.watch(leaveRequestDetailProvider(widget.requestId));

    return AppScaffold(
      title: Strings.leaveTitle,
      body: RefreshIndicator(
        onRefresh: () async =>
            ref.invalidate(leaveRequestDetailProvider(widget.requestId)),
        child: AsyncBody(
          value: detail,
          onRetry: () =>
              ref.invalidate(leaveRequestDetailProvider(widget.requestId)),
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
                        data.leaveType,
                        style: Theme.of(context).textTheme.titleLarge
                            ?.copyWith(fontWeight: FontWeight.w700),
                      ),
                    ),
                    StatusChip(status: data.status),
                  ],
                ),
                const SizedBox(height: 4),
                Text(
                  '${AppFormatters.date(data.startDate)} – '
                  '${AppFormatters.date(data.endDate)}',
                  style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                    color: Theme.of(context).colorScheme.onSurfaceVariant,
                  ),
                ),
                const SizedBox(height: 16),
                SectionCard(
                  title: Strings.summary,
                  child: Column(
                    children: [
                      InfoRow(
                        label: Strings.daysEstimated,
                        value: AppFormatters.days(data.units),
                      ),
                      InfoRow(
                        label: Strings.dayType,
                        value: AppFormatters.enumLabel(data.dayType),
                      ),
                      InfoRow(
                        label: Strings.requestedAt,
                        value: AppFormatters.timestamp(data.submittedAt),
                      ),
                      if (data.approverName != null &&
                          data.approverName!.isNotEmpty)
                        InfoRow(label: 'Approver', value: data.approverName!),
                      if (data.decidedAt != null)
                        InfoRow(
                          label: 'Decided',
                          value: AppFormatters.timestamp(data.decidedAt),
                        ),
                      InfoRow(label: Strings.reason, value: data.reason ?? '-'),
                      if (data.rejectionReason != null &&
                          data.rejectionReason!.isNotEmpty)
                        InfoRow(
                          label: 'Rejection reason',
                          value: data.rejectionReason!,
                          valueStyle: TextStyle(
                            color: Theme.of(context).colorScheme.error,
                          ),
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
                          _HistoryRow(entry: entry),
                      ],
                    ),
                  ),
                ],
                if (data.isCancellable) ...[
                  const SizedBox(height: 20),
                  SizedBox(
                    width: double.infinity,
                    child: AppButton(
                      label: Strings.cancelRequest,
                      icon: Icons.cancel_outlined,
                      outlined: true,
                      danger: true,
                      busy: _busy,
                      onPressed: _busy ? null : _cancel,
                    ),
                  ),
                ],
                if (data.approvalTaskId != null) ...[
                  const SizedBox(height: 12),
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

class _HistoryRow extends StatelessWidget {
  const _HistoryRow({required this.entry});

  final DecisionEntry entry;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return ListTile(
      contentPadding: EdgeInsets.zero,
      dense: true,
      leading: const Icon(Icons.history_rounded, size: 20),
      title: Text(
        [
          if (entry.step != null && entry.step!.isNotEmpty)
            '${AppFormatters.enumLabel(entry.step)}: ',
          if (entry.action != null) AppFormatters.enumLabel(entry.action),
          if (entry.actorName != null) '· ${entry.actorName}',
        ].join(' '),
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
