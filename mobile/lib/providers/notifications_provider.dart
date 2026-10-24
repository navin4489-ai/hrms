import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../core/network/api_client.dart';
import '../models/communication_models.dart';
import 'api_provider.dart';

/// `GET /notifications?unreadOnly=&page=` (FR-EMP-122).
/// Family key: `'all'` or `'unread'`.
final notificationsProvider =
    FutureProvider.family<List<NotificationItem>, String>((ref, filter) async {
      final api = ref.watch(apiClientProvider);
      final query = <String, dynamic>{'page': 1, 'pageSize': 50};
      if (filter == 'unread') query['unreadOnly'] = 'true';
      // §16 documents a bare array while §1 defines the paged envelope;
      // getItems accepts either shape.
      final items = await api.getItems('/notifications', query: query);
      return items
          .whereType<Map>()
          .map((e) => NotificationItem.fromJson(Map<String, dynamic>.from(e)))
          .toList(growable: false);
    });

/// `GET /notifications/unread-count` — drives the bell badge.
final unreadCountProvider = FutureProvider<int>((ref) async {
  final api = ref.watch(apiClientProvider);
  final json = await api.getMap('/notifications/unread-count');
  return UnreadCount.fromJson(json).count;
});

/// Notification actions (FR-EMP-122).
class NotificationActions {
  NotificationActions(this._api);

  final ApiClient _api;

  /// `POST /notifications/{id}/read` -> 204.
  Future<void> markRead(String id) => _api.postEmpty('/notifications/$id/read');

  /// `POST /notifications/read-all` -> 204.
  Future<void> markAllRead() => _api.postEmpty('/notifications/read-all');
}

final notificationActionsProvider = Provider<NotificationActions>(
  (ref) => NotificationActions(ref.watch(apiClientProvider)),
);
