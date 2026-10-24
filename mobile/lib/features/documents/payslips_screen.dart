import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/strings.dart';
import '../../core/utils/file_saver.dart';
import '../../core/utils/formatters.dart';
import '../../providers/payslips_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';

/// Own payslips by year (FR-EMP-060) with PDF download (FR-EMP-062).
class PayslipsScreen extends ConsumerStatefulWidget {
  const PayslipsScreen({super.key});

  @override
  ConsumerState<PayslipsScreen> createState() => _PayslipsScreenState();
}

class _PayslipsScreenState extends ConsumerState<PayslipsScreen> {
  int _year = DateTime.now().year;
  String? _downloadingId;

  List<int> get _years {
    final now = DateTime.now().year;
    return [for (var y = now; y > now - 6; y--) y];
  }

  Future<void> _download(String id, String label) async {
    setState(() => _downloadingId = id);
    try {
      final bytes = await ref.read(payrollActionsProvider).downloadPayslip(id);
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
      if (mounted) setState(() => _downloadingId = null);
    }
  }

  @override
  Widget build(BuildContext context) {
    final payslips = ref.watch(payslipsProvider(_year));

    return AppScaffold(
      title: Strings.payslips,
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(payslipsProvider(_year)),
        child: Column(
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 12, 16, 8),
              child: DropdownButtonFormField<int>(
                initialValue: _year,
                decoration: const InputDecoration(
                  labelText: 'Year',
                  prefixIcon: Icon(Icons.calendar_today_rounded, size: 18),
                ),
                items: [
                  for (final year in _years)
                    DropdownMenuItem(value: year, child: Text('$year')),
                ],
                onChanged: (value) {
                  if (value != null) setState(() => _year = value);
                },
              ),
            ),
            Expanded(
              child: AsyncBody(
                value: payslips,
                onRetry: () => ref.invalidate(payslipsProvider(_year)),
                isEmpty: (items) => items.isEmpty,
                emptyTitle: Strings.noPayslips,
                emptyMessage: Strings.noPayslipsHint,
                emptyIcon: Icons.receipt_long_outlined,
                builder: (items) => ListView.separated(
                  physics: const AlwaysScrollableScrollPhysics(),
                  padding: const EdgeInsets.fromLTRB(16, 4, 16, 16),
                  itemCount: items.length,
                  separatorBuilder: (_, __) => const SizedBox(height: 10),
                  itemBuilder: (context, index) {
                    final payslip = items[index];
                    return Card(
                      clipBehavior: Clip.antiAlias,
                      child: ListTile(
                        leading: const Icon(Icons.receipt_long_rounded),
                        title: Text(payslip.periodLabel),
                        subtitle: Text(
                          '${Strings.netPay}: '
                          '${AppFormatters.currency(payslip.netPay, payslip.currency)}',
                        ),
                        trailing: _downloadingId == payslip.id
                            ? const SizedBox(
                                width: 20,
                                height: 20,
                                child: CircularProgressIndicator(
                                  strokeWidth: 2,
                                ),
                              )
                            : IconButton(
                                tooltip: Strings.download,
                                icon: const Icon(Icons.download_rounded),
                                onPressed: () =>
                                    _download(payslip.id, payslip.periodLabel),
                              ),
                        onTap: () => context.go('/more/payslips/${payslip.id}'),
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
