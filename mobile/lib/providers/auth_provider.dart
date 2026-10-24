import 'dart:convert';
import 'dart:io' show Platform;

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../core/network/api_exception.dart';
import '../models/auth_models.dart';
import 'api_provider.dart';

/// Lifecycle of the signed-in session.
enum AuthStatus {
  /// App start: tokens being restored from secure storage.
  restoring,

  /// No valid session — show login.
  unauthenticated,

  /// Credentials accepted; an MFA code is required.
  mfaRequired,

  /// Signed in.
  authenticated,

  /// Credentials correct but the account is suspended/terminated (403).
  blocked,
}

class AuthState {
  const AuthState({
    this.status = AuthStatus.restoring,
    this.user,
    this.mfaToken,
    this.blockedReason,
    this.sessionExpired = false,
  });

  final AuthStatus status;
  final UserPrincipal? user;
  final String? mfaToken;
  final String? blockedReason;
  final bool sessionExpired;

  bool get isAuthenticated => status == AuthStatus.authenticated;
  bool get isRestoring => status == AuthStatus.restoring;

  /// FR-EMP-014 UI gate.
  bool hasPermission(String code) => user?.hasPermission(code) ?? false;

  bool hasAnyPermission(Iterable<String> codes) =>
      user?.hasAnyPermission(codes) ?? false;

  AuthState copyWith({
    AuthStatus? status,
    UserPrincipal? user,
    String? mfaToken,
    String? blockedReason,
    bool? sessionExpired,
  }) => AuthState(
    status: status ?? this.status,
    user: user ?? this.user,
    mfaToken: mfaToken ?? this.mfaToken,
    blockedReason: blockedReason ?? this.blockedReason,
    sessionExpired: sessionExpired ?? this.sessionExpired,
  );
}

/// Controls the auth session: restore, login, MFA verify, logout.
class AuthController extends Notifier<AuthState> {
  static const _kCachedUser = 'wintech.cached_user';

  @override
  AuthState build() {
    // Wire the 401-refresh-failed callback before any request can fire.
    final api = ref.read(apiClientProvider);
    api.onSessionExpired = handleSessionExpired;
    return const AuthState();
  }

  /// Called once at app start: restores tokens + cached user (FR-EMP-005).
  Future<void> restoreSession() async {
    final storage = ref.read(tokenStorageProvider);
    final access = await storage.readAccessToken();
    final refresh = await storage.readRefreshToken();
    if (access == null || refresh == null) {
      state = const AuthState(status: AuthStatus.unauthenticated);
      return;
    }
    final cached = await _readCachedUser();
    if (cached == null) {
      await storage.clear();
      state = const AuthState(status: AuthStatus.unauthenticated);
      return;
    }
    state = AuthState(status: AuthStatus.authenticated, user: cached);
  }

  /// `POST /auth/login` (FR-EMP-001). Throws [ApiException] for the UI to
  /// render inline (401 generic, 422 field errors, 429 retry-after…).
  Future<void> login({
    required String username,
    required String password,
  }) async {
    final api = ref.read(apiClientProvider);
    try {
      final data = await api.postMap(
        '/auth/login',
        data: {
          'username': username.trim(),
          'password': password,
          'deviceName': deviceName(),
        },
      );
      final result = AuthResult.fromJson(data);
      if (result.mfaRequired) {
        state = AuthState(
          status: AuthStatus.mfaRequired,
          mfaToken: result.mfaToken,
        );
        return;
      }
      await _completeLogin(result);
    } on ApiException catch (e) {
      if (e.isAccountInactive) {
        state = AuthState(
          status: AuthStatus.blocked,
          blockedReason: e.detail ?? e.title,
        );
        return;
      }
      rethrow;
    }
  }

  /// `POST /auth/mfa/verify` (FR-EMP-003).
  Future<void> verifyMfa(String code) async {
    final mfaToken = state.mfaToken;
    if (mfaToken == null) {
      state = const AuthState(status: AuthStatus.unauthenticated);
      return;
    }
    final api = ref.read(apiClientProvider);
    final data = await api.postMap(
      '/auth/mfa/verify',
      data: {'mfaToken': mfaToken, 'code': code},
    );
    await _completeLogin(AuthResult.fromJson(data));
  }

  /// Back from MFA screen to login without verifying.
  void cancelMfa() {
    state = const AuthState(status: AuthStatus.unauthenticated);
  }

  /// `POST /auth/logout` — idempotent; local clear always happens.
  Future<void> logout() async {
    final api = ref.read(apiClientProvider);
    final storage = ref.read(tokenStorageProvider);
    final refresh = await storage.readRefreshToken();
    if (refresh != null) {
      try {
        await api.postEmpty('/auth/logout', data: {'refreshToken': refresh});
      } catch (_) {
        // Logout is idempotent — local sign-out proceeds regardless.
      }
    }
    await _clearLocal();
    state = const AuthState(
      status: AuthStatus.unauthenticated,
      sessionExpired: false,
    );
  }

  /// Called by [ApiClient] when a silent refresh fails: the session is dead
  /// (contract §20: clear stored tokens, return to login).
  void handleSessionExpired() {
    _clearLocal();
    state = const AuthState(
      status: AuthStatus.unauthenticated,
      sessionExpired: true,
    );
  }

  /// Clears a transient "session expired" flag after login screen consumed it.
  void acknowledgeSessionExpired() {
    if (state.sessionExpired) {
      state = state.copyWith(sessionExpired: false);
    }
  }

  Future<void> _completeLogin(AuthResult result) async {
    final access = result.accessToken;
    final refresh = result.refreshToken;
    final user = result.user;
    if (access == null || refresh == null || user == null) {
      throw const ApiException(
        statusCode: 500,
        title: 'Unexpected response',
        detail: 'The server did not return a complete session.',
      );
    }
    await ref
        .read(tokenStorageProvider)
        .saveTokens(accessToken: access, refreshToken: refresh);
    await _cacheUser(user);
    state = AuthState(status: AuthStatus.authenticated, user: user);
  }

  Future<void> _clearLocal() async {
    await ref.read(tokenStorageProvider).clear();
    final prefs = await SharedPreferences.getInstance();
    await prefs.remove(_kCachedUser);
  }

  Future<void> _cacheUser(UserPrincipal user) async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(_kCachedUser, jsonEncode(user.toJson()));
  }

  Future<UserPrincipal?> _readCachedUser() async {
    final prefs = await SharedPreferences.getInstance();
    final raw = prefs.getString(_kCachedUser);
    if (raw == null) return null;
    try {
      return UserPrincipal.fromJson(jsonDecode(raw) as Map<String, dynamic>);
    } catch (_) {
      return null;
    }
  }

  /// Human-readable device label sent with login (contract: deviceName).
  static String deviceName() {
    try {
      final os = Platform.operatingSystem;
      switch (os) {
        case 'android':
          return 'Android device';
        case 'ios':
          return 'iPhone';
        case 'windows':
          return 'Windows PC';
        case 'macos':
          return 'Mac';
        case 'linux':
          return 'Linux PC';
        default:
          return 'Mobile device';
      }
    } catch (_) {
      return 'Mobile device';
    }
  }
}

final authControllerProvider = NotifierProvider<AuthController, AuthState>(
  AuthController.new,
);

/// Convenience read of the current [AuthState].
final authStateProvider = Provider<AuthState>(
  (ref) => ref.watch(authControllerProvider),
);
