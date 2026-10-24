import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../core/network/api_client.dart';
import '../models/communication_models.dart';
import 'api_provider.dart';

/// `GET /announcements?category=&page=` (FR-EMP-120).
/// Family key: category filter (`''` = all).
final announcementsProvider = FutureProvider.family<List<Announcement>, String>(
  (ref, category) async {
    final api = ref.watch(apiClientProvider);
    final query = <String, dynamic>{'page': 1, 'pageSize': 50};
    if (category.isNotEmpty) query['category'] = category;
    // §16 documents a bare array while §1 defines the paged envelope;
    // getItems accepts either shape.
    final items = await api.getItems('/announcements', query: query);
    return items
        .whereType<Map>()
        .map((e) => Announcement.fromJson(Map<String, dynamic>.from(e)))
        .toList(growable: false);
  },
);

/// `GET /announcements/{id}`.
final announcementDetailProvider = FutureProvider.family<Announcement, String>((
  ref,
  id,
) async {
  final api = ref.watch(apiClientProvider);
  final json = await api.getMap('/announcements/$id');
  return Announcement.fromJson(json);
});

/// Announcement actions.
class AnnouncementActions {
  AnnouncementActions(this._api);

  final ApiClient _api;

  /// `POST /announcements/{id}/acknowledge` -> 204 (409 if not required).
  Future<void> acknowledge(String id) =>
      _api.postEmpty('/announcements/$id/acknowledge');

  /// `POST /announcements` -> draft (FR-ADM-130).
  Future<Map<String, dynamic>> create(Map<String, dynamic> request) =>
      _api.postMap('/announcements', data: request);

  /// `POST /announcements/{id}/publish` -> publish and notify the audience.
  Future<void> publish(String id) => _api.postMap('/announcements/$id/publish');
}

final announcementActionsProvider = Provider<AnnouncementActions>(
  (ref) => AnnouncementActions(ref.watch(apiClientProvider)),
);
