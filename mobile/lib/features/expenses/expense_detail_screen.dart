import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/network/api_exception.dart';
import '../../core/strings.dart';
import '../../core/utils/formatters.dart';
import '../../providers/expenses_provider.dart';
import '../../widgets/app_button.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';
import '../../widgets/confirm_dialog.dart';
import '../../widgets/section_card.dart';
import '../../widgets/status_chip.dart';

/// Expense claim detail with cancellation (FR-EMP-084).
class ExpenseDetailScreen extends ConsumerStatefulWidget {
  const ExpenseDetailScreen({super.key, required this.expenseId});

  final String expenseId;

  @override
  ConsumerState<ExpenseDetailScreen> createState() =>
      _ExpenseDetailScreenState();
}

class _ExpenseDetailScreenState extends ConsumerState<ExpenseDetailScreen> {
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
            hintText: 'Why are you cancelling this claim?',
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
      title: 'Cancel expense',
      message: 'This will withdraw your expense claim. Continue?',
      confirmLabel: 'Cancel claim',
      destructive: true,
    );
    if (!confirmed || !mounted) return;
    final reason = await _promptReason();
    if (reason == null || !mounted) return;

    setState(() => _busy = true);
    try {
      await ref
          .read(expenseActionsProvider)
          .cancelExpense(id: widget.expenseId, reason: reason);
      ref.invalidate(expenseDetailProvider(widget.expenseId));
      ref.invalidate(expensesProvider);
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Expense claim cancelled')),
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
    final detail = ref.watch(expenseDetailProvider(widget.expenseId));

    return AppScaffold(
      title: Strings.expenses,
      body: RefreshIndicator(
        onRefresh: () async =>
            ref.invalidate(expenseDetailProvider(widget.expenseId)),
        child: AsyncBody(
          value: detail,
          onRetry: () =>
              ref.invalidate(expenseDetailProvider(widget.expenseId)),
          builder: (expense) => SingleChildScrollView(
            physics: const AlwaysScrollableScrollPhysics(),
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Expanded(
                      child: Text(
                        AppFormatters.currency(
                          expense.amount,
                          expense.currency,
                        ),
                        style: Theme.of(context).textTheme.headlineSmall
                            ?.copyWith(fontWeight: FontWeight.w800),
                      ),
                    ),
                    StatusChip(status: expense.status),
                  ],
                ),
                const SizedBox(height: 4),
                Text(
                  expense.category,
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
                        label: Strings.expenseDate,
                        value: AppFormatters.date(expense.expenseDate),
                      ),
                      InfoRow(
                        label: Strings.businessPurpose,
                        value: expense.businessPurpose ?? '-',
                      ),
                      if (expense.costCentre != null)
                        InfoRow(
                          label: Strings.costCentreLabel,
                          value: expense.costCentre!,
                        ),
                      if (expense.submittedAt != null)
                        InfoRow(
                          label: Strings.requestedAt,
                          value: AppFormatters.timestamp(expense.submittedAt),
                        ),
                      if (expense.approvedAt != null)
                        InfoRow(
                          label: 'Approved',
                          value: AppFormatters.timestamp(expense.approvedAt),
                        ),
                      if (expense.paidReference != null)
                        InfoRow(
                          label: 'Payment reference',
                          value: expense.paidReference!,
                        ),
                      InfoRow(
                        label: Strings.receipt,
                        value: expense.receiptUrl != null
                            ? Strings.yes
                            : Strings.no,
                      ),
                      if (expense.rejectionReason != null &&
                          expense.rejectionReason!.isNotEmpty)
                        InfoRow(
                          label: 'Rejection reason',
                          value: expense.rejectionReason!,
                          valueStyle: TextStyle(
                            color: Theme.of(context).colorScheme.error,
                          ),
                        ),
                    ],
                  ),
                ),
                if (expense.canCancel) ...[
                  const SizedBox(height: 20),
                  SizedBox(
                    width: double.infinity,
                    child: AppButton(
                      label: 'Cancel claim',
                      icon: Icons.cancel_outlined,
                      outlined: true,
                      danger: true,
                      busy: _busy,
                      onPressed: _busy ? null : _cancel,
                    ),
                  ),
                ],
                if (expense.approvalTaskId != null) ...[
                  const SizedBox(height: 12),
                  SizedBox(
                    width: double.infinity,
                    child: OutlinedButton.icon(
                      onPressed: () =>
                          context.go('/approvals/${expense.approvalTaskId!}'),
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
