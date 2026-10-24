import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/strings.dart';
import '../../core/utils/formatters.dart';
import '../../providers/cases_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';
import '../../widgets/status_chip.dart';

/// My helpdesk cases (FR-EMP-112) with status filter and FAQ entry point.
class CasesScreen extends ConsumerStatefulWidget {
  const CasesScreen({super.key});

  @override
  ConsumerState<CasesScreen> createState() => _CasesScreenState();
}

class _CasesScreenState extends ConsumerState<CasesScreen> {
  String? _status;

  static const _filters = <String?>[
    null,
    'Open',
    'WaitingOnEmployee',
    'WaitingOnHR',
    'Resolved',
    'Closed',
  ];

  @override
  Widget build(BuildContext context) {
    final query = CaseQuery(status: _status);
    final cases = ref.watch(casesProvider(query));

    return AppScaffold(
      title: Strings.myCases,
      actions: [
        IconButton(
          tooltip: Strings.faq,
          icon: const Icon(Icons.menu_book_outlined),
          onPressed: () => context.go('/more/cases/faq'),
        ),
      ],
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => context.go('/more/cases/new'),
        icon: const Icon(Icons.add_rounded),
        label: const Text(Strings.newCase),
      ),
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(casesProvider(query)),
        child: Column(
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 12, 16, 4),
              child: SingleChildScrollView(
                scrollDirection: Axis.horizontal,
                child: Row(
                  children: [
                    for (final status in _filters)
                      Padding(
                        padding: const EdgeInsets.only(right: 8),
                        child: ChoiceChip(
                          label: Text(
                            status == null
                                ? Strings.all
                                : AppFormatters.enumLabel(status),
                          ),
                          selected: _status == status,
                          onSelected: (_) => setState(() => _status = status),
                        ),
                      ),
                  ],
                ),
              ),
            ),
            Expanded(
              child: AsyncBody(
                value: cases,
                onRetry: () => ref.invalidate(casesProvider(query)),
                isEmpty: (page) => page.items.isEmpty,
                emptyTitle: Strings.noCases,
                emptyMessage: Strings.noCasesHint,
                emptyIcon: Icons.support_agent_outlined,
                builder: (page) {
                  final items = page.items;
                  if (items.isEmpty) {
                    return ListView(
                      physics: const AlwaysScrollableScrollPhysics(),
                      padding: const EdgeInsets.all(16),
                      children: const [
                        SizedBox(height: 120),
                        Center(
                          child: Text(
                            Strings.noResults,
                            style: TextStyle(fontWeight: FontWeight.w600),
                          ),
                        ),
                        SizedBox(height: 6),
                        Center(child: Text(Strings.noResultsHint)),
                      ],
                    );
                  }
                  return ListView.separated(
                    physics: const AlwaysScrollableScrollPhysics(),
                    padding: const EdgeInsets.fromLTRB(16, 8, 16, 88),
                    itemCount: items.length,
                    separatorBuilder: (_, __) => const SizedBox(height: 10),
                    itemBuilder: (context, index) {
                      final item = items[index];
                      return Card(
                        clipBehavior: Clip.antiAlias,
                        child: ListTile(
                          leading: const Icon(Icons.support_agent_outlined),
                          title: Text(
                            '${item.ticketNumber} · ${item.subject}',
                            maxLines: 2,
                            overflow: TextOverflow.ellipsis,
                          ),
                          subtitle: Text(
                            [
                              if (item.category != null) item.category!,
                              AppFormatters.relativeTime(
                                item.updatedAt ?? item.createdAt,
                              ),
                            ].join(' · '),
                          ),
                          trailing: StatusChip(
                            status: item.status,
                            dense: true,
                          ),
                          onTap: () => context.go('/more/cases/${item.id}'),
                        ),
                      );
                    },
                  );
                },
              ),
            ),
          ],
        ),
      ),
    );
  }
}
