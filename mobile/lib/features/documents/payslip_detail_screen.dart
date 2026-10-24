import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/strings.dart';
import '../../core/utils/file_saver.dart';
import '../../core/utils/formatters.dart';
import '../../providers/payslips_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';
import '../../widgets/section_card.dart';

/// Payslip detail: earnings, deductions, totals and PDF download.
class PayslipDetailScreen extends ConsumerStatefulWidget {
  const PayslipDetailScreen({super.key, required this.payslipId});

  final String payslipId;

  @override
  ConsumerState<PayslipDetailScreen> createState() =>
      _PayslipDetailScreenState();
}

class _PayslipDetailScreenState extends ConsumerState<PayslipDetailScreen> {
  bool _downloading = false;

  Future<void> _download(String label) async {
    setState(() => _downloading = true);
    try {
      final bytes = await ref
          .read(payrollActionsProvider)
          .downloadPayslip(widget.payslipId);
      final path = await saveBytesToDevice(bytes, 'payslip-$label.pdf');
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('${Strings.downloadedTo} $path')),
        );
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text('$e')));
      }
    } finally {
      if (mounted) setState(() => _downloading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final detail = ref.watch(payslipDetailProvider(widget.payslipId));

    return AppScaffold(
      title: Strings.payslips,
      body: RefreshIndicator(
        onRefresh: () async =>
            ref.invalidate(payslipDetailProvider(widget.payslipId)),
        child: AsyncBody(
          value: detail,
          onRetry: () =>
              ref.invalidate(payslipDetailProvider(widget.payslipId)),
          builder: (payslip) => SingleChildScrollView(
            physics: const AlwaysScrollableScrollPhysics(),
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  payslip.periodLabel,
                  style: Theme.of(context).textTheme.titleLarge
                      ?.copyWith(fontWeight: FontWeight.w700),
                ),
                const SizedBox(height: 4),
                Text(
                  '${AppFormatters.date(payslip.startDate)} – '
                  '${AppFormatters.date(payslip.endDate)}',
                  style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                    color: Theme.of(context).colorScheme.onSurfaceVariant,
                  ),
                ),
                const SizedBox(height: 16),
                SectionCard(
                  title: Strings.summary,
                  child: Column(
                    children: [
                      if (payslip.employeeName != null)
                        InfoRow(
                          label: 'Employee',
                          value: payslip.employeeName!,
                        ),
                      if (payslip.employeeNumber != null)
                        InfoRow(
                          label: 'Employee number',
                          value: payslip.employeeNumber!,
                        ),
                      if (payslip.legalEntity != null)
                        InfoRow(
                          label: Strings.legalEntity,
                          value: payslip.legalEntity!,
                        ),
                      InfoRow(
                        label: Strings.gross,
                        value: AppFormatters.currency(
                          payslip.gross,
                          payslip.currency,
                        ),
                      ),
                      InfoRow(
                        label: Strings.totalDeductions,
                        value: AppFormatters.currency(
                          payslip.totalDeductions,
                          payslip.currency,
                        ),
                      ),
                      InfoRow(
                        label: Strings.netPay,
                        value: AppFormatters.currency(
                          payslip.netPay,
                          payslip.currency,
                        ),
                        valueStyle: TextStyle(
                          fontWeight: FontWeight.w800,
                          color: Theme.of(context).colorScheme.primary,
                        ),
                      ),
                      if (payslip.ytdGross != null)
                        InfoRow(
                          label: 'YTD gross',
                          value: AppFormatters.currency(
                            payslip.ytdGross,
                            payslip.currency,
                          ),
                        ),
                    ],
                  ),
                ),
                if (payslip.earnings.isNotEmpty) ...[
                  const SizedBox(height: 12),
                  SectionCard(
                    title: Strings.earnings,
                    child: Column(
                      children: [
                        for (final component in payslip.earnings)
                          InfoRow(
                            label: component.component,
                            value: AppFormatters.currency(
                              component.amount,
                              payslip.currency,
                            ),
                          ),
                      ],
                    ),
                  ),
                ],
                if (payslip.deductions.isNotEmpty) ...[
                  const SizedBox(height: 12),
                  SectionCard(
                    title: Strings.deductions,
                    child: Column(
                      children: [
                        for (final component in payslip.deductions)
                          InfoRow(
                            label: component.component,
                            value: AppFormatters.currency(
                              component.amount,
                              payslip.currency,
                            ),
                          ),
                      ],
                    ),
                  ),
                ],
                const SizedBox(height: 20),
                SizedBox(
                  width: double.infinity,
                  child: FilledButton.icon(
                    onPressed: _downloading
                        ? null
                        : () => _download(payslip.periodLabel),
                    icon: _downloading
                        ? const SizedBox(
                            width: 18,
                            height: 18,
                            child: CircularProgressIndicator(strokeWidth: 2.4),
                          )
                        : const Icon(Icons.download_rounded, size: 18),
                    label: const Text(Strings.download),
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
