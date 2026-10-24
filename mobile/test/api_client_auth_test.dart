import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:wintech_hrms/core/network/api_client.dart';
import 'package:wintech_hrms/core/network/api_exception.dart';
import 'package:wintech_hrms/core/storage/token_storage.dart';

/// Fake transport: any non-refresh call with a stale token gets a 401; after
/// a successful refresh the new access token is accepted.
class _FakeAdapter implements HttpClientAdapter {
  int refreshCalls = 0;
  int dataCalls = 0;
  bool refreshSucceeds = true;

  /// Reject data calls even when the presented token is fresh — used to
  /// exercise the "second 401 after refresh" force-logout path.
  bool rejectDataAlways = false;
  final List<String?> authHeaders = <String?>[];
  final List<String?> correlationIds = <String?>[];

  static const _jsonHeaders = {
    Headers.contentTypeHeader: [Headers.jsonContentType],
  };

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    correlationIds.add(options.headers['X-Correlation-Id'] as String?);

    if (options.path.endsWith('/auth/refresh')) {
      refreshCalls++;
      // Give concurrent callers time to pile onto the single-flight future.
      await Future<void>.delayed(const Duration(milliseconds: 25));
      if (!refreshSucceeds) {
        return ResponseBody.fromString(
          jsonEncode(const {
            'status': 401,
            'title': 'Unauthorized',
            'detail': 'Refresh token is invalid or expired.',
          }),
          401,
          headers: _jsonHeaders,
        );
      }
      return ResponseBody.fromString(
        jsonEncode(const {
          'accessToken': 'new-access',
          'refreshToken': 'new-refresh',
        }),
        200,
        headers: _jsonHeaders,
      );
    }

    dataCalls++;
    final auth = options.headers['Authorization'] as String?;
    authHeaders.add(auth);
    if (auth == 'Bearer new-access' && !rejectDataAlways) {
      return ResponseBody.fromString(
        jsonEncode(const {'id': 'e-1', 'displayName': 'Ada'}),
        200,
        headers: _jsonHeaders,
      );
    }
    return ResponseBody.fromString(
      jsonEncode(const {
        'status': 401,
        'title': 'Unauthorized',
        'detail': 'Access token has expired.',
      }),
      401,
      headers: _jsonHeaders,
    );
  }

  @override
  void close({bool force = false}) {}
}

void main() {
  late InMemoryTokenStorage storage;
  late _FakeAdapter adapter;
  late ApiClient client;

  setUp(() {
    storage = InMemoryTokenStorage()
      ..accessToken = 'stale-access'
      ..refreshToken = 'stale-refresh';
    adapter = _FakeAdapter();
    client = ApiClient(
      tokenStorage: storage,
      baseUrl: 'http://localhost:5080',
      httpClientAdapter: adapter,
    );
  });

  tearDown(() => client.close());

  test('attaches the bearer token and a UUIDv4 correlation id', () async {
    // Access token already valid for this test: use the new-token path.
    await storage.saveTokens(
      accessToken: 'new-access',
      refreshToken: 'stale-refresh',
    );

    final data = await client.getMap('/me');

    expect(data['id'], 'e-1');
    expect(adapter.authHeaders, contains('Bearer new-access'));
    final correlation = adapter.correlationIds.single;
    expect(correlation, isNotEmpty);
    expect(
      RegExp(
        r'^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-'
        r'[0-9a-f]{12}$',
      ).hasMatch(correlation ?? ''),
      isTrue,
      reason: 'X-Correlation-Id must be a UUIDv4',
    );
  });

  test('concurrent 401s share a single refresh and then retry', () async {
    final results = await Future.wait([
      client.getMap('/me'),
      client.getMap('/me'),
      client.getMap('/me'),
    ]);

    expect(results, hasLength(3));
    for (final result in results) {
      expect(result['displayName'], 'Ada');
    }
    expect(
      adapter.refreshCalls,
      1,
      reason: 'refresh must be single-flight under concurrency',
    );
    expect(adapter.dataCalls, 6, reason: '3 stale + 3 retried requests');
    expect(await storage.readAccessToken(), 'new-access');
    expect(await storage.readRefreshToken(), 'new-refresh');
  });

  test(
    'refresh failure clears the session and notifies the listener',
    () async {
      adapter.refreshSucceeds = false;
      var expired = false;
      client.onSessionExpired = () => expired = true;

      await expectLater(
        client.getMap('/me'),
        throwsA(isA<ApiException>().having((e) => e.statusCode, 'status', 401)),
      );

      // clear() is chained with whenComplete; let the microtask run.
      await Future<void>.delayed(Duration.zero);
      expect(expired, isTrue);
      expect(await storage.readAccessToken(), isNull);
      expect(await storage.readRefreshToken(), isNull);
    },
  );

  test(
    'a retry that is still 401 forces logout without a second refresh',
    () async {
      adapter.rejectDataAlways = true;
      var expired = false;
      client.onSessionExpired = () => expired = true;

      await expectLater(
        client.getMap('/me'),
        throwsA(isA<ApiException>().having((e) => e.statusCode, 'status', 401)),
      );

      // clear() is chained with whenComplete; let the microtask run.
      await Future<void>.delayed(Duration.zero);
      expect(
        adapter.refreshCalls,
        1,
        reason: 'the retried request must not trigger another refresh',
      );
      expect(expired, isTrue);
      expect(await storage.readAccessToken(), isNull);
      expect(await storage.readRefreshToken(), isNull);
    },
  );

  test('helpers surface validation envelopes as ApiException.errors', () async {
    final errorAdapter = _EnvelopeAdapter(
      statusCode: 422,
      body: const {
        'status': 422,
        'title': 'Validation failed',
        'detail': 'One or more fields are invalid.',
        'errors': {
          'amount': ['Amount exceeds the per-day limit'],
        },
        'correlationId': 'cid-1',
      },
    );
    final c = ApiClient(
      tokenStorage: storage,
      baseUrl: 'http://localhost:5080',
      httpClientAdapter: errorAdapter,
    );
    addTearDown(c.close);

    await expectLater(
      c.postMap('/expenses', data: const {'amount': 9999}),
      throwsA(
        isA<ApiException>()
            .having((e) => e.statusCode, 'statusCode', 422)
            .having(
              (e) => e.fieldErrors('amount')?.first,
              'amountError',
              'Amount exceeds the per-day limit',
            )
            .having((e) => e.correlationId, 'correlationId', 'cid-1'),
      ),
    );
  });
}

/// Adapter that always answers with a fixed error envelope.
class _EnvelopeAdapter implements HttpClientAdapter {
  _EnvelopeAdapter({required this.statusCode, required this.body});

  final int statusCode;
  final Map<String, dynamic> body;

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    return ResponseBody.fromString(
      jsonEncode(body),
      statusCode,
      headers: const {
        Headers.contentTypeHeader: [Headers.jsonContentType],
      },
    );
  }

  @override
  void close({bool force = false}) {}
}
