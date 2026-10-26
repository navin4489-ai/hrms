import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/network/api_exception.dart';
import '../../core/strings.dart';
import '../../providers/admin_payroll_provider.dart';
import '../../providers/admin_provider.dart';
import '../../providers/api_provider.dart';
import '../../providers/auth_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';
import '../../widgets/status_chip.dart';

class AdminPayrollPeriodsScreen extends ConsumerWidget {
  const AdminPayrollPeriodsScreen({super.key});

  String _isoDate(DateTime date) =>
      "${date.year.toString().padLeft(4, '0')}-"
      "${date.month.toString().padLeft(2, '0')}-"
      "${date.day.toString().padLeft(2, '0')}";

  String _displayDate(DateTime date) =>
      "${date.year.toString().padLeft(4, '0')}-"
      "${date.month.toString().padLeft(2, '0')}-"
      "${date.day.toString().padLeft(2, '0')}";

  Future<void> _createPeriod(
    BuildContext context,
    WidgetRef ref,
    List<AdminPayrollPeriod> periods,
  ) async {
    final latestEnd = periods
        .map((period) => DateTime.tryParse(period.endDate))
        .whereType<DateTime>()
        .fold<DateTime?>(
          null,
          (latest, date) =>
              latest == null || date.isAfter(latest) ? date : latest,
        );
    final initialStart = latestEnd == null
        ? DateTime(DateTime.now().year, DateTime.now().month, 1)
        : DateTime(latestEnd.year, latestEnd.month + 1, 1);
    var start = initialStart;
    var end = DateTime(start.year, start.month + 1, 0);
    var cutoff = end.add(const Duration(days: 5));

    final result = await showDialog<Map<String, DateTime>>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) => AlertDialog(
          title: const Text('Create payroll period'),
          content: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Text(
                'Create an open period first, then import and validate the payroll CSV.',
              ),
              const SizedBox(height: 12),
              _DateChoice(
                label: 'Start date',
                date: start,
                onTap: () async {
                  final date = await showDatePicker(
                    context: dialogContext,
                    initialDate: start,
                    firstDate: DateTime(2000),
                    lastDate: DateTime(2100),
                  );
                  if (date == null) return;
                  setDialogState(() {
                    start = date;
                    end = DateTime(start.year, start.month + 1, 0);
                    cutoff = end.add(const Duration(days: 5));
                  });
                },
                formatDate: _displayDate,
              ),
              _DateChoice(
                label: 'End date',
                date: end,
                onTap: () async {
                  final date = await showDatePicker(
                    context: dialogContext,
                    initialDate: end,
                    firstDate: start,
                    lastDate: DateTime(2100),
                  );
                  if (date == null) return;
                  setDialogState(() {
                    end = date;
                    cutoff = end.add(const Duration(days: 5));
                  });
                },
                formatDate: _displayDate,
              ),
              _DateChoice(
                label: 'Input cut-off date',
                date: cutoff,
                onTap: () async {
                  final date = await showDatePicker(
                    context: dialogContext,
                    initialDate: cutoff.isBefore(end) ? end : cutoff,
                    firstDate: end,
                    lastDate: DateTime(2100),
                  );
                  if (date != null) setDialogState(() => cutoff = date);
                },
                formatDate: _displayDate,
              ),
            ],
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(dialogContext),
              child: const Text(Strings.cancel),
            ),
            FilledButton(
              onPressed: () {
                if (end.isBefore(start) || cutoff.isBefore(end)) return;
                Navigator.pop(dialogContext, {
                  'startDate': start,
                  'endDate': end,
                  'cutOffDate': cutoff,
                });
              },
              child: const Text('Create period'),
            ),
          ],
        ),
      ),
    );
    if (result == null || !context.mounted) return;

    try {
      await ref
          .read(apiClientProvider)
          .postMap(
            '/payroll-periods',
            data: {
              'startDate': _isoDate(result['startDate']!),
              'endDate': _isoDate(result['endDate']!),
              'cutOffDate': _isoDate(result['cutOffDate']!),
            },
          );
      ref.invalidate(adminPayrollPeriodsProvider);
      if (context.mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(const SnackBar(content: Text('Payroll period created')));
      }
    } catch (error) {
      if (context.mounted) {
        final message = error is ApiException ? error.message : '$error';
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Could not create payroll period: $message')),
        );
      }
    }
  }

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

  /// Auto-calculate payslips for this period from every employee's pay details.
  Future<void> _generatePayslips(
    BuildContext context,
    WidgetRef ref,
    AdminPayrollPeriod period,
  ) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('Generate payslips'),
        content: Text(
          'Calculate payslips for ${period.label} from each employee\'s pay '
          'details: earnings, PF, ESI, professional tax and income tax (TDS) '
          'are computed automatically.\n\nEmployees without pay details, and '
          'anyone who already has a payslip for this period, are skipped.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(dialogContext, false),
            child: const Text(Strings.cancel),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(dialogContext, true),
            child: const Text('Generate'),
          ),
        ],
      ),
    );
    if (confirmed != true || !context.mounted) return;
    try {
      final result = await ref
          .read(adminPayrollActionsProvider)
          .generatePayslips(period.id);
      ref.invalidate(adminPayrollPeriodsProvider);
      if (context.mounted) {
        final generated = result['generated'] ?? 0;
        final skipped = result['skipped'] ?? 0;
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(
              'Generated $generated payslip(s); skipped $skipped',
            ),
          ),
        );
      }
    } catch (error) {
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Could not generate payslips: $error')),
        );
      }
    }
  }

  /// Publish generated (draft) payslips so employees can see them. Requires a reason.
  Future<void> _publishPayslips(
    BuildContext context,
    WidgetRef ref,
    AdminPayrollPeriod period,
  ) async {
    final reason = await _askReason(context, 'Publish');
    if (reason == null || !context.mounted) return;
    try {
      final result = await ref
          .read(apiClientProvider)
          .postMap(
            '/payroll-periods/${period.id}/payslips/publish',
            data: {'reason': reason},
          );
      ref.invalidate(adminPayrollPeriodsProvider);
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(
              'Published ${result['publishedCount'] ?? 0} payslip(s)',
            ),
          ),
        );
      }
    } catch (error) {
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Could not publish payslips: $error')),
        );
      }
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final periods = ref.watch(adminPayrollPeriodsProvider);
    final canManagePayroll = ref
        .watch(authStateProvider)
        .hasPermission('payroll.manage');
    final canPublish = ref
        .watch(authStateProvider)
        .hasPermission('payroll.publish');
    return AppScaffold(
      title: 'Payroll periods',
      actions: [
        if (canManagePayroll)
          IconButton(
            tooltip: 'Salary & tax',
            onPressed: () => context.push('/more/admin/payroll/salaries'),
            icon: const Icon(Icons.account_balance_outlined),
          ),
        if (canManagePayroll)
          IconButton(
            tooltip: 'Create payroll period',
            onPressed: periods.value == null
                ? null
                : () => _createPeriod(context, ref, periods.value!),
            icon: const Icon(Icons.add),
          ),
      ],
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
            separatorBuilder: (_, _) => const SizedBox(height: 8),
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
                      if (period.status == 'Open' && canPublish)
                        IconButton(
                          tooltip: 'Publish payslips to employees',
                          onPressed: () => _publishPayslips(
                            context,
                            ref,
                            period,
                          ),
                          icon: const Icon(Icons.publish_outlined),
                        ),
                      if (period.status == 'Open' && canManagePayroll)
                        IconButton(
                          tooltip: 'Generate payslips from pay details',
                          onPressed: () => _generatePayslips(
                            context,
                            ref,
                            period,
                          ),
                          icon: const Icon(Icons.calculate_outlined),
                        ),
                      if (period.status == 'Open')
                        IconButton(
                          tooltip: 'Import payroll output',
                          onPressed: () => context.push(
                            '/more/admin/payroll-periods/${period.id}/import?label=${Uri.encodeQueryComponent(period.label)}',
                          ),
                          icon: const Icon(Icons.upload_file_outlined),
                        ),
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

class _DateChoice extends StatelessWidget {
  const _DateChoice({
    required this.label,
    required this.date,
    required this.onTap,
    required this.formatDate,
  });

  final String label;
  final DateTime date;
  final VoidCallback onTap;
  final String Function(DateTime) formatDate;

  @override
  Widget build(BuildContext context) => ListTile(
    contentPadding: EdgeInsets.zero,
    title: Text(label),
    subtitle: Text(formatDate(date)),
    trailing: const Icon(Icons.calendar_month_outlined),
    onTap: onTap,
  );
}
