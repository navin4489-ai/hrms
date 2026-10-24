import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../core/network/api_client.dart';
import '../models/auth_models.dart';
import '../models/me_models.dart';
import 'api_provider.dart';

/// `GET /me` (FR-EMP-020).
final meProvider = FutureProvider<MeProfile>((ref) async {
  final api = ref.watch(apiClientProvider);
  final json = await api.getMap('/me');
  return MeProfile.fromJson(json);
});

/// `GET /me/employment-history` (FR-EMP-025).
final employmentHistoryProvider = FutureProvider<List<EmploymentHistoryEntry>>((
  ref,
) async {
  final api = ref.watch(apiClientProvider);
  final list = await api.getList('/me/employment-history');
  return list
      .whereType<Map>()
      .map((e) => EmploymentHistoryEntry.fromJson(Map<String, dynamic>.from(e)))
      .toList(growable: false);
});

/// `GET /auth/sessions` (FR-EMP-004).
final sessionsProvider = FutureProvider<List<SessionInfo>>((ref) async {
  final api = ref.watch(apiClientProvider);
  final list = await api.getList('/auth/sessions');
  return list
      .whereType<Map>()
      .map((e) => SessionInfo.fromJson(Map<String, dynamic>.from(e)))
      .toList(growable: false);
});

/// Profile-domain actions.
class ProfileActions {
  ProfileActions(this._api);

  final ApiClient _api;

  /// `PUT /me/profile` — non-sensitive fields only (FR-EMP-021).
  Future<void> updateProfile({
    String? preferredName,
    String? phone,
    String? personalEmail,
    String? address,
    String? emergencyContactName,
    String? emergencyContactPhone,
  }) async {
    await _api.putMap(
      '/me/profile',
      data: {
        'preferredName': preferredName,
        'phone': phone,
        'personalEmail': personalEmail,
        'address': address,
        'emergencyContactName': emergencyContactName,
        'emergencyContactPhone': emergencyContactPhone,
      },
    );
  }

  /// `POST /me/sensitive-change-requests` (FR-EMP-022) -> 202.
  Future<void> requestSensitiveChange({
    required String field,
    required String requestedValue,
    required String reason,
  }) async {
    await _api.postMap(
      '/me/sensitive-change-requests',
      data: {
        'field': field,
        'requestedValue': requestedValue,
        'reason': reason,
      },
    );
  }

  /// `POST /auth/change-password` -> 204.
  Future<void> changePassword({
    required String currentPassword,
    required String newPassword,
  }) async {
    await _api.postEmpty(
      '/auth/change-password',
      data: {'currentPassword': currentPassword, 'newPassword': newPassword},
    );
  }

  /// `DELETE /auth/sessions/{id}` -> 204 (FR-EMP-004).
  Future<void> revokeSession(String sessionId) async {
    await _api.delete('/auth/sessions/$sessionId');
  }
}

final profileActionsProvider = Provider<ProfileActions>(
  (ref) => ProfileActions(ref.watch(apiClientProvider)),
);
