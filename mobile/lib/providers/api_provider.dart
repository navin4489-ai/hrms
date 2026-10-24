import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../core/network/api_client.dart';
import '../core/storage/token_storage.dart';

/// OS secure storage with shared_preferences fallback (FR-EMP-005).
final tokenStorageProvider = Provider<TokenStorage>((ref) {
  final storage = SecureTokenStorage();
  ref.onDispose(() {});
  return storage;
});

/// The single API client for the app. Session-expired handling is wired by
/// the auth controller (see `auth_provider.dart`).
final apiClientProvider = Provider<ApiClient>((ref) {
  final client = ApiClient(tokenStorage: ref.watch(tokenStorageProvider));
  ref.onDispose(client.close);
  return client;
});
