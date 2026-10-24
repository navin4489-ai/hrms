import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/strings.dart';
import '../../core/utils/formatters.dart';
import '../../providers/documents_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';
import '../../widgets/status_chip.dart';

/// Published policies (FR-EMP-074) with acknowledgement filter.
class PoliciesScreen extends ConsumerStatefulWidget {
  const PoliciesScreen({super.key});

  @override
  ConsumerState<PoliciesScreen> createState() => _PoliciesScreenState();
}

class _PoliciesScreenState extends ConsumerState<PoliciesScreen> {
  String _filter = '';

  static const _filters = <String, String>{
    '': Strings.all,
    'Pending': 'To acknowledge',
    'Acknowledged': Strings.acknowledged,
  };

  @override
  Widget build(BuildContext context) {
    final policies = ref.watch(policiesProvider(_filter));

    return AppScaffold(
      title: Strings.policies,
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(policiesProvider(_filter)),
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
                          selected: _filter == entry.key,
                          onSelected: (_) =>
                              setState(() => _filter = entry.key),
                        ),
                      ),
                  ],
                ),
              ),
            ),
            Expanded(
              child: AsyncBody(
                value: policies,
                onRetry: () => ref.invalidate(policiesProvider(_filter)),
                isEmpty: (items) => items.isEmpty,
                emptyTitle: Strings.noPolicies,
                emptyMessage: 'Published company policies will appear here.',
                emptyIcon: Icons.policy_outlined,
                builder: (items) => ListView.separated(
                  physics: const AlwaysScrollableScrollPhysics(),
                  padding: const EdgeInsets.fromLTRB(16, 8, 16, 16),
                  itemCount: items.length,
                  separatorBuilder: (_, __) => const SizedBox(height: 10),
                  itemBuilder: (context, index) {
                    final policy = items[index];
                    return Card(
                      clipBehavior: Clip.antiAlias,
                      child: ListTile(
                        leading: Icon(
                          policy.needsAcknowledgement
                              ? Icons.notification_important_outlined
                              : Icons.policy_outlined,
                        ),
                        title: Text(policy.title),
                        subtitle: Text(
                          [
                            if (policy.category != null) policy.category!,
                            if (policy.version != null) 'v${policy.version}',
                            if (policy.effectiveFrom != null)
                              'Effective '
                                  '${AppFormatters.date(policy.effectiveFrom)}',
                          ].join(' · '),
                        ),
                        trailing: policy.needsAcknowledgement
                            ? const StatusChip(status: 'Pending', dense: true)
                            : policy.acknowledgedAt != null
                            ? const StatusChip(status: 'Completed', dense: true)
                            : const Icon(Icons.chevron_right_rounded),
                        onTap: () => context.go('/more/policies/${policy.id}'),
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
