import 'package:dio/dio.dart';

/// Typed exception mapped from the API error envelope:
/// `{ status, title, detail, errors, correlationId }`.
class ApiException implements Exception {
  const ApiException({
    this.statusCode,
    this.title = 'Something went wrong',
    this.detail,
    this.errors = const {},
    this.correlationId,
    this.retryAfterSeconds,
  });

  /// Maps a [DioException] with a server response to an [ApiException].
  factory ApiException.fromDio(DioException exception) {
    final response = exception.response;
    if (response == null) {
      return const ApiException(
        title: 'Connection failed',
        detail:
            'Could not reach the server. Check your connection and try again.',
      );
    }
    final data = response.data;
    if (data is Map<String, dynamic>) {
      final rawErrors = data['errors'];
      final errors = <String, List<String>>{};
      if (rawErrors is Map) {
        rawErrors.forEach((key, value) {
          if (value is List) {
            errors[key.toString()] = value
                .map((e) => e.toString())
                .toList(growable: false);
          }
        });
      }
      final retryAfter = response.headers.value('retry-after');
      return ApiException(
        statusCode: response.statusCode,
        title: (data['title'] as String?) ?? 'Request failed',
        detail: data['detail'] as String?,
        errors: errors,
        correlationId: data['correlationId'] as String?,
        retryAfterSeconds: int.tryParse(retryAfter ?? ''),
      );
    }
    return ApiException(
      statusCode: response.statusCode,
      title: 'Request failed',
      detail: 'The server returned an unexpected response.',
      correlationId: response.headers.value('x-correlation-id'),
    );
  }

  /// HTTP status code, or null for connectivity errors.
  final int? statusCode;

  /// Short human-readable title from the server.
  final String title;

  /// Optional longer detail from the server.
  final String? detail;

  /// Field-level validation errors (`errors` map from the envelope).
  final Map<String, List<String>> errors;

  /// Correlation id echoed by the server (for support).
  final String? correlationId;

  /// Seconds to wait, populated from `Retry-After` on 429.
  final int? retryAfterSeconds;

  bool get isUnauthorized => statusCode == 401;
  bool get isForbidden => statusCode == 403;
  bool get isNotFound => statusCode == 404;
  bool get isConflict => statusCode == 409;
  bool get isValidation => statusCode == 400 || statusCode == 422;
  bool get isTooManyRequests => statusCode == 429;

  /// FR-EMP-006 / contract §20: 403 with "Account not active".
  bool get isAccountInactive =>
      statusCode == 403 && title.toLowerCase().contains('not active');

  /// Field errors for a specific form field (case-insensitive key match).
  List<String>? fieldErrors(String field) {
    final direct = errors[field];
    if (direct != null) return direct;
    for (final entry in errors.entries) {
      if (entry.key.toLowerCase() == field.toLowerCase()) return entry.value;
    }
    return null;
  }

  /// First field error across all fields, useful as a banner fallback.
  String? get firstFieldError {
    for (final list in errors.values) {
      if (list.isNotEmpty) return list.first;
    }
    return null;
  }

  /// Best-effort user-facing message.
  String get message => detail ?? firstFieldError ?? title;

  @override
  String toString() =>
      'ApiException($statusCode, $title, $detail, correlationId: $correlationId)';
}
