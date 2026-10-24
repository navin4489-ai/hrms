import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../core/network/api_client.dart';
import '../models/approvals_models.dart';
import 'api_provider.dart';

/// `GET /approvals/pending` — tasks awaiting me (FR-MGR-001).
/// Family key: page number (1-based).
final pendingApprovalsProvider = FutureProvider.family<PagedApprovalList, int>((
  ref,
  page,
) async {
  final api = ref.watch(apiClientProvider);
  final json = await api.getMap(
    '/approvals/pending',
    query: {'page': page, 'pageSize': 20},
  );
  return PagedApprovalList.fromJson(json);
});

/// Paged envelope for pending approvals.
class PagedApprovalList {
  const PagedApprovalList({
    required this.items,
    required this.page,
    required this.total,
    required this.hasNextPage,
  });

  factory PagedApprovalList.fromJson(Map<String, dynamic> json) {
    final rawItems = json['items'];
    return PagedApprovalList(
      items: (rawItems is List ? rawItems : const [])
          .whereType<Map>()
          .map((e) => ApprovalTask.fromJson(Map<String, dynamic>.from(e)))
          .toList(growable: false),
      page: (json['page'] as num?)?.toInt() ?? 1,
      total: (json['total'] as num?)?.toInt() ?? 0,
      hasNextPage: json['hasNextPage'] as bool? ?? false,
    );
  }

  final List<ApprovalTask> items;
  final int page;
  final int total;
  final bool hasNextPage;
}

/// `GET /approvals/stats`.
final approvalStatsProvider = FutureProvider<ApprovalStats>((ref) async {
  final api = ref.watch(apiClientProvider);
  final json = await api.getMap('/approvals/stats');
  return ApprovalStats.fromJson(json);
});

/// `GET /approvals/mine` — my own requests' workflow state.
final myApprovalsProvider = FutureProvider<List<MyWorkflowTask>>((ref) async {
  final api = ref.watch(apiClientProvider);
  final list = await api.getList('/approvals/mine');
  return list
      .whereType<Map>()
      .map((e) => MyWorkflowTask.fromJson(Map<String, dynamic>.from(e)))
      .toList(growable: false);
});

/// `GET /approvals/{taskId}` detail + history.
final approvalDetailProvider =
    FutureProvider.family<ApprovalTaskDetail, String>((ref, taskId) async {
      final api = ref.watch(apiClientProvider);
      final json = await api.getMap('/approvals/$taskId');
      return ApprovalTaskDetail.fromJson(json);
    });

/// Approval decision actions (FR-MGR-002, BR-004/BR-007).
class ApprovalActions {
  ApprovalActions(this._api);

  final ApiClient _api;

  /// `POST /approvals/{taskId}/decision`.
  /// [comment] is mandatory for Reject/Return (BR-007) — screens disable
  /// submit until non-empty and the server enforces it with 422 as well.
  Future<DecisionResult> decide({
    required String taskId,
    required String action,
    String? comment,
  }) async {
    final json = await _api.postMap(
      '/approvals/$taskId/decision',
      data: {'action': action, 'comment': comment},
    );
    return DecisionResult.fromJson(json);
  }
}

final approvalActionsProvider = Provider<ApprovalActions>(
  (ref) => ApprovalActions(ref.watch(apiClientProvider)),
);
