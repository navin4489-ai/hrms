import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// Stores auth tokens, preferring OS secure storage and falling back to
/// [SharedPreferences] if secure storage is unavailable (FR-EMP-005).
abstract class TokenStorage {
  Future<String?> readAccessToken();
  Future<String?> readRefreshToken();
  Future<void> saveTokens({
    required String accessToken,
    required String refreshToken,
  });
  Future<void> clear();
}

/// Concrete storage: flutter_secure_storage with a shared_preferences fallback.
class SecureTokenStorage implements TokenStorage {
  SecureTokenStorage({FlutterSecureStorage? secureStorage})
    : _secure = secureStorage ?? const FlutterSecureStorage();

  static const _kAccess = 'wintech.access_token';
  static const _kRefresh = 'wintech.refresh_token';

  final FlutterSecureStorage _secure;
  bool _useFallback = false;

  @override
  Future<String?> readAccessToken() => _read(_kAccess);

  @override
  Future<String?> readRefreshToken() => _read(_kRefresh);

  @override
  Future<void> saveTokens({
    required String accessToken,
    required String refreshToken,
  }) async {
    await _write(_kAccess, accessToken);
    await _write(_kRefresh, refreshToken);
  }

  @override
  Future<void> clear() async {
    await _delete(_kAccess);
    await _delete(_kRefresh);
  }

  Future<String?> _read(String key) async {
    if (!_useFallback) {
      try {
        return await _secure.read(key: key);
      } catch (_) {
        _useFallback = true;
      }
    }
    final prefs = await SharedPreferences.getInstance();
    return prefs.getString(key);
  }

  Future<void> _write(String key, String value) async {
    if (!_useFallback) {
      try {
        await _secure.write(key: key, value: value);
        return;
      } catch (_) {
        _useFallback = true;
      }
    }
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(key, value);
  }

  Future<void> _delete(String key) async {
    if (!_useFallback) {
      try {
        await _secure.delete(key: key);
      } catch (_) {
        _useFallback = true;
      }
    }
    final prefs = await SharedPreferences.getInstance();
    await prefs.remove(key);
  }
}

/// In-memory storage for tests.
class InMemoryTokenStorage implements TokenStorage {
  String? accessToken;
  String? refreshToken;

  @override
  Future<String?> readAccessToken() async => accessToken;

  @override
  Future<String?> readRefreshToken() async => refreshToken;

  @override
  Future<void> saveTokens({
    required String accessToken,
    required String refreshToken,
  }) async {
    this.accessToken = accessToken;
    this.refreshToken = refreshToken;
  }

  @override
  Future<void> clear() async {
    accessToken = null;
    refreshToken = null;
  }
}
