import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../core/network/api_client.dart';
import '../models/case_models.dart';
import 'api_provider.dart';

/// `GET /cases/categories` (FR-EMP-111).
final caseCategoriesProvider = FutureProvider<List<CaseCategory>>((ref) async {
  final api = ref.watch(apiClientProvider);
  final list = await api.getList('/cases/categories');
  return list
      .whereType<Map>()
      .map((e) => CaseCategory.fromJson(Map<String, dynamic>.from(e)))
      .toList(growable: false);
});

/// Filter for `GET /cases`.
class CaseQuery {
  const CaseQuery({
    this.status,
    this.category,
    this.priority,
    this.queue,
    this.mine = true,
  });

  final String? status;
  final String? category;
  final String? priority;
  final String? queue;
  final bool mine;

  @override
  bool operator ==(Object other) =>
      other is CaseQuery &&
      other.status == status &&
      other.category == category &&
      other.priority == priority &&
      other.queue == queue &&
      other.mine == mine;

  @override
  int get hashCode => Object.hash(status, category, priority, queue, mine);
}

/// `GET /cases` paged (FR-EMP-112).
class CasePage {
  const CasePage({
    required this.items,
    required this.page,
    required this.total,
    required this.hasNextPage,
  });

  factory CasePage.fromJson(Map<String, dynamic> json) {
    final rawItems = json['items'];
    return CasePage(
      items: (rawItems is List ? rawItems : const [])
          .whereType<Map>()
          .map((e) => CaseItem.fromJson(Map<String, dynamic>.from(e)))
          .toList(growable: false),
      page: (json['page'] as num?)?.toInt() ?? 1,
      total: (json['total'] as num?)?.toInt() ?? 0,
      hasNextPage: json['hasNextPage'] as bool? ?? false,
    );
  }

  final List<CaseItem> items;
  final int page;
  final int total;
  final bool hasNextPage;
}

final casesProvider = FutureProvider.family<CasePage, CaseQuery>((
  ref,
  query,
) async {
  final api = ref.watch(apiClientProvider);
  final params = <String, dynamic>{
    'page': 1,
    'pageSize': 50,
    if (query.mine) 'mine': 'true',
  };
  if (query.status != null) params['status'] = query.status;
  if (query.category != null) params['category'] = query.category;
  if (query.priority != null) params['priority'] = query.priority;
  if (query.queue != null) params['queue'] = query.queue;
  final json = await api.getMap('/cases', query: params);
  return CasePage.fromJson(json);
});

/// `GET /cases/{id}` detail with messages (FR-EMP-112/113).
final caseDetailProvider = FutureProvider.family<CaseDetail, String>((
  ref,
  id,
) async {
  final api = ref.watch(apiClientProvider);
  final json = await api.getMap('/cases/$id');
  return CaseDetail.fromJson(json);
});

/// `GET /knowledge-base?q=` (FR-EMP-115).
/// Family key: search query ('' = all).
final knowledgeBaseProvider = FutureProvider.family<List<KbArticle>, String>((
  ref,
  q,
) async {
  final api = ref.watch(apiClientProvider);
  final query = <String, dynamic>{};
  if (q.trim().isNotEmpty) query['q'] = q.trim();
  final list = await api.getList('/knowledge-base', query: query);
  return list
      .whereType<Map>()
      .map((e) => KbArticle.fromJson(Map<String, dynamic>.from(e)))
      .toList(growable: false);
});

/// `GET /knowledge-base/{id}`.
final kbArticleProvider = FutureProvider.family<KbArticleDetail, String>((
  ref,
  id,
) async {
  final api = ref.watch(apiClientProvider);
  final json = await api.getMap('/knowledge-base/$id');
  return KbArticleDetail.fromJson(json);
});

/// Helpdesk actions (FR-EMP-110..113).
class CaseActions {
  CaseActions(this._api);

  final ApiClient _api;

  /// `POST /cases` -> 201.
  Future<CaseItem> createCase({
    required String categoryId,
    String? subcategoryId,
    required String subject,
    required String description,
    required String urgency,
    String? attachmentUrl,
  }) async {
    final json = await _api.postMap(
      '/cases',
      data: {
        'categoryId': categoryId,
        if (subcategoryId != null) 'subcategoryId': subcategoryId,
        'subject': subject,
        'description': description,
        'urgency': urgency,
        if (attachmentUrl != null && attachmentUrl.isNotEmpty)
          'attachmentUrl': attachmentUrl,
      },
    );
    return CaseItem.fromJson(json);
  }

  /// `POST /cases/{id}/messages` -> 201 (employee reply, FR-EMP-113).
  Future<void> reply({required String caseId, required String body}) async {
    await _api.postMap('/cases/$caseId/messages', data: {'body': body});
  }

  /// `POST /cases/{id}/notes` -> 201 (HR-internal note, never shown to the
  /// employee; requires case.manage, FR-ADM-122).
  Future<void> addNote({required String caseId, required String body}) async {
    await _api.postMap('/cases/$caseId/notes', data: {'body': body});
  }

  /// `POST /cases/{id}/transition` — Reopen for employees within the window.
  Future<void> transition({
    required String caseId,
    required String action,
    String? comment,
  }) async {
    await _api.postMap(
      '/cases/$caseId/transition',
      data: {
        'action': action,
        if (comment != null && comment.isNotEmpty) 'comment': comment,
      },
    );
  }

  /// `POST /cases/{id}/rate` -> 200 (once, after resolve — FR-EMP-113).
  Future<void> rate({
    required String caseId,
    required int rating,
    String? comment,
  }) async {
    await _api.postMap(
      '/cases/$caseId/rate',
      data: {
        'rating': rating,
        if (comment != null && comment.isNotEmpty) 'comment': comment,
      },
    );
  }
}

final caseActionsProvider = Provider<CaseActions>(
  (ref) => CaseActions(ref.watch(apiClientProvider)),
);
