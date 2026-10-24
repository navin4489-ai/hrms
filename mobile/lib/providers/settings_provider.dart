import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../core/network/api_client.dart';
import '../models/me_models.dart';
import 'api_provider.dart';

/// `GET /me/notification-preferences` (FR-EMP-121).
final notificationPreferencesProvider =
    FutureProvider<List<NotificationPreference>>((ref) async {
      final api = ref.watch(apiClientProvider);
      final list = await api.getList('/me/notification-preferences');
      return list
          .whereType<Map>()
          .map(
            (e) =>
                NotificationPreference.fromJson(Map<String, dynamic>.from(e)),
          )
          .toList(growable: false);
    });

/// Settings-domain actions.
class SettingsActions {
  SettingsActions(this._api);

  final ApiClient _api;

  /// `PUT /me/notification-preferences` -> 204.
  /// Mandatory items must stay enabled (server enforces with 422).
  Future<void> saveNotificationPreferences(
    List<NotificationPreference> items,
  ) async {
    await _api.putMap(
      '/me/notification-preferences',
      data: {'items': items.map((e) => e.toJson()).toList(growable: false)},
    );
  }

  /// `POST /auth/forgot-password` -> 202 always (FR-EMP-002, no leak).
  Future<void> forgotPassword(String username) async {
    await _api.postEmpty(
      '/auth/forgot-password',
      data: {'username': username.trim()},
    );
  }

  /// `POST /auth/reset-password` -> 204 | 400 invalid/expired.
  Future<void> resetPassword({
    required String token,
    required String newPassword,
  }) async {
    await _api.postEmpty(
      '/auth/reset-password',
      data: {'token': token, 'newPassword': newPassword},
    );
  }
}

final settingsActionsProvider = Provider<SettingsActions>(
  (ref) => SettingsActions(ref.watch(apiClientProvider)),
);
