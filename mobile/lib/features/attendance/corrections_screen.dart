import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/strings.dart';
import '../../core/utils/formatters.dart';
import '../../providers/attendance_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';
import '../../widgets/status_chip.dart';

/// My attendance correction requests with status filter (FR-EMP-036).
class CorrectionsScreen extends ConsumerStatefulWidget {
  const CorrectionsScreen({super.key});

  @override
  ConsumerState<CorrectionsScreen> createState() => _CorrectionsScreenState();
}

class _CorrectionsScreenState extends ConsumerState<CorrectionsScreen> {
  String _status = '';

  static const _filters = <String, String>{
    '': Strings.all,
    'Pending': Strings.pending,
    'Approved': 'Approved',
    'Rejected': 'Rejected',
  };

  @override
  Widget build(BuildContext context) {
    final corrections = ref.watch(correctionsProvider(_status));

    return AppScaffold(
      title: Strings.myCorrections,
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => context.go('/attendance/corrections/new'),
        icon: const Icon(Icons.add_rounded),
        label: const Text(Strings.newCorrection),
      ),
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(correctionsProvider(_status)),
        child: Column(
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 12, 16, 4),
              child: SingleChildScrollView(
                scrollDirection: Axis.horizontal,
                child: Row(
                  children: [
                    for (final entry in _filters.entries)
                      Padding(
                        padding: const EdgeInsets.only(right: 8),
                        child: ChoiceChip(
                          label: Text(entry.value),
                          selected: _status == entry.key,
                          onSelected: (_) =>
                              setState(() => _status = entry.key),
                        ),
                      ),
                  ],
                ),
              ),
            ),
            Expanded(
              child: AsyncBody(
                value: corrections,
                onRetry: () => ref.invalidate(correctionsProvider(_status)),
                isEmpty: (items) => items.isEmpty,
                emptyTitle: Strings.noCorrections,
                emptyMessage: Strings.noCorrectionsHint,
                emptyIcon: Icons.fact_check_outlined,
                builder: (items) => ListView.separated(
                  physics: const AlwaysScrollableScrollPhysics(),
                  padding: const EdgeInsets.fromLTRB(16, 8, 16, 88),
                  itemCount: items.length,
                  separatorBuilder: (_, __) => const SizedBox(height: 10),
                  itemBuilder: (context, index) {
                    final item = items[index];
                    return Card(
                      clipBehavior: Clip.antiAlias,
                      child: ListTile(
                        title: Text(AppFormatters.date(item.workDate)),
                        subtitle: Text(
                          [
                            AppFormatters.enumLabel(item.type),
                            if (item.reason != null && item.reason!.isNotEmpty)
                              item.reason!,
                          ].join(' · '),
                          maxLines: 2,
                          overflow: TextOverflow.ellipsis,
                        ),
                        trailing: StatusChip(status: item.status, dense: true),
                        onTap: () =>
                            context.go('/attendance/corrections/${item.id}'),
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
