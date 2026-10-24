import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/strings.dart';
import '../../providers/admin_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';
import '../../widgets/status_chip.dart';

class AdminPayrollPeriodsScreen extends ConsumerWidget {
  const AdminPayrollPeriodsScreen({super.key});

  Future<String?> _askReason(BuildContext context, String action) async {
    final controller = TextEditingController();
    final reason = await showDialog<String>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: Text('$action payroll period'),
        content: TextField(
          controller: controller,
          minLines: 2,
          maxLines: 4,
          decoration: const InputDecoration(
            labelText: 'Reason',
            hintText: 'Required for the audit record',
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(dialogContext).pop(),
            child: const Text(Strings.cancel),
          ),
          FilledButton(
            onPressed: () {
              final value = controller.text.trim();
              if (value.isNotEmpty) Navigator.of(dialogContext).pop(value);
            },
            child: Text(action),
          ),
        ],
      ),
    );
    controller.dispose();
    return reason;
  }

  Future<void> _setPeriodState(
    BuildContext context,
    WidgetRef ref,
    AdminPayrollPeriod period,
  ) async {
    final lock = period.status == 'Open';
    final reason = await _askReason(context, lock ? 'Lock' : 'Reopen');
    if (reason == null || !context.mounted) return;
    try {
      final actions = ref.read(adminUserActionsProvider);
      if (lock) {
        await actions.lockPayrollPeriod(period.id, reason);
      } else {
        await actions.unlockPayrollPeriod(period.id, reason);
      }
      ref.invalidate(adminPayrollPeriodsProvider);
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text('Payroll period ${lock ? 'locked' : 'reopened'}'),
          ),
        );
      }
    } catch (error) {
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Payroll action failed: $error')),
        );
      }
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final periods = ref.watch(adminPayrollPeriodsProvider);
    return AppScaffold(
      title: 'Payroll periods',
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(adminPayrollPeriodsProvider),
        child: AsyncBody(
          value: periods,
          onRetry: () => ref.invalidate(adminPayrollPeriodsProvider),
          isEmpty: (data) => data.isEmpty,
          emptyTitle: 'No payroll periods',
          emptyMessage: 'Payroll periods have not been configured.',
          emptyIcon: Icons.payments_outlined,
          builder: (data) => ListView.separated(
            physics: const AlwaysScrollableScrollPhysics(),
            padding: const EdgeInsets.all(16),
            itemCount: data.length,
            separatorBuilder: (_, __) => const SizedBox(height: 8),
            itemBuilder: (context, index) {
              final period = data[index];
              final canChange =
                  period.status == 'Open' || period.status == 'Locked';
              return Card(
                child: ListTile(
                  leading: const Icon(Icons.calendar_month_outlined),
                  title: Text(period.label),
                  subtitle: Text(
                    '${period.startDate} – ${period.endDate}'
                    '${period.cutOffDate == null ? '' : '\nCut-off: ${period.cutOffDate}'}',
                  ),
                  isThreeLine: period.cutOffDate != null,
                  trailing: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      StatusChip(status: period.status, dense: true),
                      if (canChange)
                        IconButton(
                          tooltip: period.status == 'Open'
                              ? 'Lock period'
                              : 'Reopen period',
                          onPressed: () =>
                              _setPeriodState(context, ref, period),
                          icon: Icon(
                            period.status == 'Open'
                                ? Icons.lock_outline_rounded
                                : Icons.lock_open_rounded,
                          ),
                        ),
                    ],
                  ),
                ),
              );
            },
          ),
        ),
      ),
    );
  }
}
