import 'dart:async';
import 'dart:convert';
import 'dart:math';

import 'package:dio/dio.dart';

import '../config/env.dart';
import '../storage/token_storage.dart';
import 'api_exception.dart';

/// Single Dio-backed API client for the Wintech HRMS REST API.
///
/// Responsibilities:
/// - attaches `Authorization: Bearer <token>` from [TokenStorage]
/// - attaches a per-request `X-Correlation-Id`
/// - single-flight refresh-on-401: concurrent 401s share one refresh call,
///   queued requests wait and then retry with the new token
/// - notifies [onSessionExpired] when refresh fails (caller must log out)
/// - maps every error to [ApiException]
class ApiClient {
  ApiClient({
    required TokenStorage tokenStorage,
    String? baseUrl,
    HttpClientAdapter? httpClientAdapter,
  }) : _storage = tokenStorage {
    dio = Dio(
      BaseOptions(
        baseUrl: (baseUrl ?? Env.apiBaseUrl) + Env.apiPrefix,
        connectTimeout: const Duration(seconds: 15),
        receiveTimeout: const Duration(seconds: 30),
        headers: {'Content-Type': 'application/json'},
        responseType: ResponseType.json,
      ),
    );
    // Bare client for refresh calls — no auth interceptors, no recursion.
    _refreshDio = Dio(
      BaseOptions(baseUrl: (baseUrl ?? Env.apiBaseUrl) + Env.apiPrefix),
    );
    if (httpClientAdapter != null) {
      dio.httpClientAdapter = httpClientAdapter;
      _refreshDio.httpClientAdapter = httpClientAdapter;
    }
    dio.interceptors.add(
      InterceptorsWrapper(onRequest: _onRequest, onError: _onError),
    );
  }

  /// Fired when the refresh token is missing/expired/invalid. The listener
  /// must clear the session and route the user to login.
  void Function()? onSessionExpired;

  late final Dio dio;
  late final Dio _refreshDio;
  final TokenStorage _storage;

  Future<bool>? _refreshInFlight;
  static const _kRetried = 'auth_retried';

  // ---------------------------------------------------------------------------
  // Interceptors
  // ---------------------------------------------------------------------------

  Future<void> _onRequest(
    RequestOptions options,
    RequestInterceptorHandler handler,
  ) async {
    final token = await _storage.readAccessToken();
    if (token != null && token.isNotEmpty) {
      options.headers['Authorization'] = 'Bearer $token';
    }
    options.headers['X-Correlation-Id'] = _newCorrelationId();
    handler.next(options);
  }

  Future<void> _onError(
    DioException err,
    ErrorInterceptorHandler handler,
  ) async {
    final status = err.response?.statusCode;
    final options = err.requestOptions;
    final isAuthRoute =
        options.path.startsWith('/auth/login') ||
        options.path.startsWith('/auth/refresh') ||
        options.path.startsWith('/auth/mfa') ||
        options.path.startsWith('/auth/forgot-password');
    final alreadyRetried = options.extra[_kRetried] == true;

    if (status == 401 && !isAuthRoute && !alreadyRetried) {
      final refreshed = await _refreshTokens();
      if (refreshed) {
        // Retry the original request once with the fresh token.
        options.extra[_kRetried] = true;
        final token = await _storage.readAccessToken();
        if (token != null) {
          options.headers['Authorization'] = 'Bearer $token';
        }
        try {
          final response = await dio.fetch<dynamic>(options);
          handler.resolve(response);
          return;
        } on DioException catch (retryError) {
          handler.next(retryError);
          return;
        }
      }
      // Refresh failed — session is dead.
      _storage.clear().whenComplete(() => onSessionExpired?.call());
    } else if (status == 401 && !isAuthRoute && alreadyRetried) {
      // Second 401 even with a fresh token: force logout.
      _storage.clear().whenComplete(() => onSessionExpired?.call());
    }
    handler.next(err);
  }

  /// Single-flight token refresh: concurrent callers share one in-flight
  /// refresh future. Returns true when a valid token pair is available.
  Future<bool> _refreshTokens() {
    return _refreshInFlight ??= _doRefresh().whenComplete(() {
      _refreshInFlight = null;
    });
  }

  Future<bool> _doRefresh() async {
    final refreshToken = await _storage.readRefreshToken();
    if (refreshToken == null || refreshToken.isEmpty) return false;
    try {
      final response = await _refreshDio.post<Map<String, dynamic>>(
        '/auth/refresh',
        data: {'refreshToken': refreshToken},
        options: Options(headers: {'Authorization': null}),
      );
      final data = response.data;
      if (response.statusCode == 200 && data != null) {
        final access = data['accessToken'] as String?;
        final refresh = data['refreshToken'] as String?;
        if (access == null || refresh == null) return false;
        await _storage.saveTokens(accessToken: access, refreshToken: refresh);
        return true;
      }
      return false;
    } catch (_) {
      return false;
    }
  }

  // ---------------------------------------------------------------------------
  // Request helpers — all throw [ApiException] on non-2xx
  // ---------------------------------------------------------------------------

  Future<Map<String, dynamic>> getMap(
    String path, {
    Map<String, dynamic>? query,
    CancelToken? cancelToken,
  }) => _guard(() async {
    final response = await dio.get<Map<String, dynamic>>(
      path,
      queryParameters: query,
      cancelToken: cancelToken,
    );
    return response.data ?? <String, dynamic>{};
  });

  Future<List<dynamic>> getList(
    String path, {
    Map<String, dynamic>? query,
    CancelToken? cancelToken,
  }) => _guard(() async {
    final response = await dio.get<List<dynamic>>(
      path,
      queryParameters: query,
      cancelToken: cancelToken,
    );
    return response.data ?? <dynamic>[];
  });

  /// List items from either response shape the contract allows: a paged
  /// envelope (`{ "items": [...] }`, §1) or a bare JSON array ("plain list",
  /// §1). `/announcements` and `/notifications` are documented ambiguously —
  /// §16 shows an array while both endpoints accept `page&pageSize` — so both
  /// shapes are accepted for them.
  Future<List<dynamic>> getItems(
    String path, {
    Map<String, dynamic>? query,
    CancelToken? cancelToken,
  }) => _guard(() async {
    final response = await dio.get<dynamic>(
      path,
      queryParameters: query,
      cancelToken: cancelToken,
    );
    final data = response.data;
    if (data is List) return data;
    if (data is Map) {
      final items = data['items'];
      if (items is List) return items;
    }
    return const <dynamic>[];
  });

  Future<Map<String, dynamic>> postMap(
    String path, {
    Object? data,
    Map<String, dynamic>? query,
  }) => _guard(() async {
    final response = await dio.post<Map<String, dynamic>>(
      path,
      data: data,
      queryParameters: query,
    );
    return response.data ?? <String, dynamic>{};
  });

  Future<Map<String, dynamic>> putMap(
    String path, {
    Object? data,
    Map<String, dynamic>? query,
  }) => _guard(() async {
    final response = await dio.put<Map<String, dynamic>>(
      path,
      data: data,
      queryParameters: query,
    );
    return response.data ?? <String, dynamic>{};
  });

  /// POST returning 204/any body (returns the status code). Throws
  /// [ApiException] on non-2xx.
  Future<int> postEmpty(String path, {Object? data}) => _guard(() async {
    final response = await dio.post<void>(path, data: data);
    return response.statusCode ?? 200;
  });

  Future<int> putEmpty(String path, {Object? data}) => _guard(() async {
    final response = await dio.put<void>(path, data: data);
    return response.statusCode ?? 200;
  });

  Future<int> delete(String path, {Object? data}) => _guard(() async {
    final response = await dio.delete<void>(path, data: data);
    return response.statusCode ?? 200;
  });

  /// Downloads a file (PDF etc.) and returns the bytes.
  Future<List<int>> downloadBytes(String path) => _guard(() async {
    final response = await dio.get<List<int>>(
      path,
      options: Options(responseType: ResponseType.bytes),
    );
    return response.data ?? const <int>[];
  });

  /// Multipart POST (documents, expenses, avatar).
  Future<Map<String, dynamic>> postMultipart(
    String path, {
    required FormData formData,
    Map<String, dynamic>? query,
  }) => _guard(() async {
    final response = await dio.post<Map<String, dynamic>>(
      path,
      data: formData,
      queryParameters: query,
    );
    return response.data ?? <String, dynamic>{};
  });

  void close() {
    dio.close(force: true);
    _refreshDio.close(force: true);
  }

  // ---------------------------------------------------------------------------
  // Error mapping: every helper funnels through here so screens only ever
  // catch [ApiException].
  // ---------------------------------------------------------------------------

  Future<T> _guard<T>(Future<T> Function() run) async {
    try {
      return await run();
    } on DioException catch (e) {
      throw ApiException.fromDio(e);
    }
  }

  static final _random = Random.secure();

  static String _newCorrelationId() {
    final bytes = List<int>.generate(16, (_) => _random.nextInt(256));
    bytes[6] = (bytes[6] & 0x0f) | 0x40; // v4
    bytes[8] = (bytes[8] & 0x3f) | 0x80; // variant
    final hex = bytes.map((b) => b.toRadixString(16).padLeft(2, '0')).join();
    return '${hex.substring(0, 8)}-${hex.substring(8, 12)}-'
        '${hex.substring(12, 16)}-${hex.substring(16, 20)}-${hex.substring(20)}';
  }
}

/// Convenience: decode a JSON string body (used by tests).
Map<String, dynamic> decodeJsonMap(String source) =>
    jsonDecode(source) as Map<String, dynamic>;
