/// Paged list envelope from the API contract:
/// `{ items, page, pageSize, total, hasNextPage }`.
class PagedResult<T> {
  const PagedResult({
    required this.items,
    required this.page,
    required this.pageSize,
    required this.total,
    required this.hasNextPage,
    this.summary,
  });

  factory PagedResult.fromJson(
    Map<String, dynamic> json,
    T Function(Map<String, dynamic>) itemFromJson, {
    Map<String, dynamic> Function(Map<String, dynamic>)? summaryFromJson,
  }) {
    final rawItems = json['items'];
    return PagedResult<T>(
      items: (rawItems is List ? rawItems : const [])
          .whereType<Map>()
          .map((e) => itemFromJson(Map<String, dynamic>.from(e)))
          .toList(growable: false),
      page: (json['page'] as num?)?.toInt() ?? 1,
      pageSize: (json['pageSize'] as num?)?.toInt() ?? 20,
      total: (json['total'] as num?)?.toInt() ?? 0,
      hasNextPage: json['hasNextPage'] as bool? ?? false,
      summary: summaryFromJson == null
          ? null
          : json['summary'] is Map
          ? summaryFromJson(Map<String, dynamic>.from(json['summary'] as Map))
          : null,
    );
  }

  final List<T> items;
  final int page;
  final int pageSize;
  final int total;
  final bool hasNextPage;

  /// Attendance month summary (only present on `GET /attendance`).
  final Map<String, dynamic>? summary;

  bool get isEmpty => items.isEmpty;
}

/// Small shared parsing helpers for manual `fromJson` implementations.
abstract final class JsonX {
  static String? str(Object? value) {
    if (value == null) return null;
    if (value is String) return value.isEmpty ? null : value;
    return value.toString();
  }

  static String strOr(Object? value, [String fallback = '']) =>
      str(value) ?? fallback;

  static num? numOrNull(Object? value) =>
      value is num ? value : num.tryParse(value?.toString() ?? '');

  static int? intOrNull(Object? value) => numOrNull(value)?.toInt();

  static double? doubleOrNull(Object? value) => numOrNull(value)?.toDouble();

  static bool boolOr(Object? value, [bool fallback = false]) =>
      value is bool ? value : fallback;

  static DateTime? dateTimeOrNull(Object? value) {
    if (value is! String || value.isEmpty) return null;
    return DateTime.tryParse(value);
  }

  static List<String> stringList(Object? value) => value is List
      ? value.map((e) => e.toString()).toList(growable: false)
      : const <String>[];

  static Map<String, dynamic> mapOrNull(Object? value) =>
      value is Map ? Map<String, dynamic>.from(value) : <String, dynamic>{};

  static List<Map<String, dynamic>> mapList(Object? value) => value is List
      ? value
            .whereType<Map>()
            .map((e) => Map<String, dynamic>.from(e))
            .toList(growable: false)
      : const <Map<String, dynamic>>[];
}
