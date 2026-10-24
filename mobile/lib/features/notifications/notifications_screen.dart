import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/network/api_exception.dart';
import '../../core/strings.dart';
import '../../core/utils/formatters.dart';
import '../../models/communication_models.dart';
import '../../providers/notifications_provider.dart';
import '../../router/deep_link_parser.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';

/// Notification inbox (FR-EMP-122): unread filter, mark-read actions and
/// deep links that resolve to the owning feature screen.
class NotificationsScreen extends ConsumerStatefulWidget {
  const NotificationsScreen({super.key});

  @override
  ConsumerState<NotificationsScreen> createState() =>
      _NotificationsScreenState();
}

class _NotificationsScreenState extends ConsumerState<NotificationsScreen> {
  /// `'all'` | `'unread'` (family key of [notificationsProvider]).
  String _filter = 'all';
  String? _busyId;
  bool _busyAll = false;

  Future<void> _markAllRead() async {
    setState(() => _busyAll = true);
    try {
      await ref.read(notificationActionsProvider).markAllRead();
      ref.invalidate(notificationsProvider(_filter));
      ref.invalidate(unreadCountProvider);
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(const SnackBar(content: Text(Strings.allRead)));
      }
    } on ApiException catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text(e.message)));
      }
    } catch (_) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(const SnackBar(content: Text(Strings.offlineMessage)));
      }
    } finally {
      if (mounted) setState(() => _busyAll = false);
    }
  }

  Future<void> _open(NotificationItem item) async {
    // Mark read first so the badge and unread filter update immediately.
    if (!item.isRead) {
      setState(() => _busyId = item.id);
      try {
        await ref.read(notificationActionsProvider).markRead(item.id);
      } catch (_) {
        // Non-fatal: the notification still opens below.
      } finally {
        if (mounted) setState(() => _busyId = null);
      }
      ref.invalidate(notificationsProvider(_filter));
      ref.invalidate(unreadCountProvider);
    }
    if (!mounted) return;
    final route = DeepLinkParser.toAppRoute(item.deepLink);
    if (route != null) {
      context.go(route);
    }
  }

  @override
  Widget build(BuildContext context) {
    final notifications = ref.watch(notificationsProvider(_filter));

    return AppScaffold(
      title: Strings.notificationsTitle,
      actions: [
        if (_busyAll)
          const Padding(
            padding: EdgeInsets.all(14),
            child: SizedBox(
              width: 18,
              height: 18,
              child: CircularProgressIndicator(strokeWidth: 2.4),
            ),
          )
        else
          IconButton(
            tooltip: Strings.markAllRead,
            icon: const Icon(Icons.done_all_rounded),
            onPressed: _markAllRead,
          ),
      ],
      body: RefreshIndicator(
        onRefresh: () async {
          ref.invalidate(notificationsProvider(_filter));
          ref.invalidate(unreadCountProvider);
        },
        child: Column(
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 12, 16, 4),
              child: Row(
                children: [
                  ChoiceChip(
                    label: const Text(Strings.all),
                    selected: _filter == 'all',
                    onSelected: (_) => setState(() => _filter = 'all'),
                  ),
                  const SizedBox(width: 8),
                  ChoiceChip(
                    label: const Text(Strings.unreadOnly),
                    selected: _filter == 'unread',
                    onSelected: (_) => setState(() => _filter = 'unread'),
                  ),
                ],
              ),
            ),
            Expanded(
              child: AsyncBody(
                value: notifications,
                onRetry: () => ref.invalidate(notificationsProvider(_filter)),
                isEmpty: (items) => items.isEmpty,
                emptyTitle: Strings.noNotifications,
                emptyMessage: _filter == 'unread'
                    ? Strings.allRead
                    : Strings.noNotificationsHint,
                emptyIcon: Icons.notifications_none_rounded,
                builder: (items) => ListView.separated(
                  physics: const AlwaysScrollableScrollPhysics(),
                  padding: const EdgeInsets.fromLTRB(16, 8, 16, 16),
                  itemCount: items.length,
                  separatorBuilder: (_, __) => const SizedBox(height: 8),
                  itemBuilder: (context, index) {
                    final item = items[index];
                    final unread = !item.isRead;
                    return Card(
                      clipBehavior: Clip.antiAlias,
                      color: unread
                          ? Theme.of(context).colorScheme.secondaryContainer
                                .withValues(alpha: 0.45)
                          : null,
                      child: ListTile(
                        leading: _busyId == item.id
                            ? const SizedBox(
                                width: 22,
                                height: 22,
                                child: CircularProgressIndicator(
                                  strokeWidth: 2.4,
                                ),
                              )
                            : Icon(
                                unread
                                    ? Icons.markunread_rounded
                                    : Icons.mark_email_read_outlined,
                                color: unread
                                    ? Theme.of(context).colorScheme.primary
                                    : null,
                              ),
                        title: Text(
                          item.title,
                          maxLines: 2,
                          overflow: TextOverflow.ellipsis,
                          style: TextStyle(
                            fontWeight: unread
                                ? FontWeight.w700
                                : FontWeight.w500,
                          ),
                        ),
                        subtitle: Text(
                          [
                            if (item.body != null && item.body!.isNotEmpty)
                              item.body!,
                            AppFormatters.relativeTime(item.createdAt),
                          ].join('\n'),
                          maxLines: 3,
                          overflow: TextOverflow.ellipsis,
                        ),
                        trailing: unread
                            ? Icon(
                                Icons.fiber_manual_record_rounded,
                                size: 11,
                                color: Theme.of(context).colorScheme.primary,
                              )
                            : null,
                        onTap: () => _open(item),
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
