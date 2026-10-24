import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/strings.dart';
import '../../core/utils/formatters.dart';
import '../../providers/auth_provider.dart';
import '../../providers/announcements_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';

/// Company announcements (FR-EMP-120) with category filters.
class AnnouncementsScreen extends ConsumerStatefulWidget {
  const AnnouncementsScreen({super.key});

  @override
  ConsumerState<AnnouncementsScreen> createState() =>
      _AnnouncementsScreenState();
}

class _AnnouncementsScreenState extends ConsumerState<AnnouncementsScreen> {
  /// Contract categories (`API_CONTRACT` §16); `''` = all.
  static const _categories = <String, String>{
    '': Strings.all,
    'Company': 'Company',
    'HR': 'HR',
    'Policy': 'Policy',
    'Event': 'Event',
    'IT': 'IT',
    'Wellness': 'Wellness',
  };

  String _category = '';

  @override
  Widget build(BuildContext context) {
    final announcements = ref.watch(announcementsProvider(_category));
    final canCreate = ref
        .watch(authStateProvider)
        .hasPermission('announcement.create');

    return AppScaffold(
      title: Strings.announcementsTitle,
      floatingActionButton: canCreate
          ? FloatingActionButton.extended(
              onPressed: () => context.push('/home/announcements/new'),
              icon: const Icon(Icons.campaign_outlined),
              label: const Text('Create announcement'),
            )
          : null,
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(announcementsProvider(_category)),
        child: Column(
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 12, 16, 4),
              child: SingleChildScrollView(
                scrollDirection: Axis.horizontal,
                child: Row(
                  children: [
                    for (final entry in _categories.entries)
                      Padding(
                        padding: const EdgeInsets.only(right: 8),
                        child: ChoiceChip(
                          label: Text(entry.value),
                          selected: _category == entry.key,
                          onSelected: (_) =>
                              setState(() => _category = entry.key),
                        ),
                      ),
                  ],
                ),
              ),
            ),
            Expanded(
              child: AsyncBody(
                value: announcements,
                onRetry: () => ref.invalidate(announcementsProvider(_category)),
                isEmpty: (items) => items.isEmpty,
                emptyTitle: Strings.noAnnouncements,
                emptyMessage: Strings.noAnnouncementsHint,
                emptyIcon: Icons.campaign_outlined,
                builder: (items) => ListView.separated(
                  physics: const AlwaysScrollableScrollPhysics(),
                  padding: const EdgeInsets.fromLTRB(16, 8, 16, 16),
                  itemCount: items.length,
                  separatorBuilder: (_, __) => const SizedBox(height: 10),
                  itemBuilder: (context, index) {
                    final item = items[index];
                    return Card(
                      clipBehavior: Clip.antiAlias,
                      child: ListTile(
                        leading: Icon(
                          item.pinned
                              ? Icons.push_pin_rounded
                              : Icons.campaign_outlined,
                          color: item.pinned
                              ? Theme.of(context).colorScheme.primary
                              : null,
                        ),
                        title: Text(
                          item.title,
                          maxLines: 2,
                          overflow: TextOverflow.ellipsis,
                        ),
                        subtitle: Text(
                          [
                            if (item.category != null) item.category!,
                            AppFormatters.relativeTime(item.publishedAt),
                            if (item.needsAcknowledgement) Strings.acknowledge,
                          ].join(' · '),
                        ),
                        trailing: item.pinned
                            ? const Tooltip(
                                message: Strings.pinned,
                                child: Icon(Icons.push_pin_rounded, size: 17),
                              )
                            : null,
                        onTap: () =>
                            context.go('/home/announcements/${item.id}'),
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
