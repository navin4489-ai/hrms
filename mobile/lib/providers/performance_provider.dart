import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../core/network/api_client.dart';
import '../models/performance_models.dart';
import 'api_provider.dart';

/// `GET /performance/cycles/current` — nullable (no active cycle).
final currentCycleProvider = FutureProvider<ReviewCycle?>((ref) async {
  final api = ref.watch(apiClientProvider);
  final json = await api.getMap('/performance/cycles/current');
  if (json.isEmpty) return null;
  return ReviewCycle.fromJson(json);
});

/// `GET /performance/goals?cycleId=` (FR-EMP-100).
/// Family key: cycleId (may be empty).
final goalsProvider = FutureProvider.family<List<Goal>, String>((
  ref,
  cycleId,
) async {
  final api = ref.watch(apiClientProvider);
  final query = <String, dynamic>{};
  if (cycleId.isNotEmpty) query['cycleId'] = cycleId;
  final list = await api.getList('/performance/goals', query: query);
  return list
      .whereType<Map>()
      .map((e) => Goal.fromJson(Map<String, dynamic>.from(e)))
      .toList(growable: false);
});

/// `GET /performance/reviews` — published reviews (FR-EMP-102).
final reviewsProvider = FutureProvider<List<PerformanceReview>>((ref) async {
  final api = ref.watch(apiClientProvider);
  final list = await api.getList('/performance/reviews');
  return list
      .whereType<Map>()
      .map((e) => PerformanceReview.fromJson(Map<String, dynamic>.from(e)))
      .toList(growable: false);
});

/// `GET /training/courses?status=` (FR-EMP-103).
/// Family key: enrollment status filter (`''` = all).
final coursesProvider = FutureProvider.family<List<Course>, String>((
  ref,
  status,
) async {
  final api = ref.watch(apiClientProvider);
  final query = <String, dynamic>{};
  if (status.isNotEmpty) query['status'] = status;
  final list = await api.getList('/training/courses', query: query);
  return list
      .whereType<Map>()
      .map((e) => Course.fromJson(Map<String, dynamic>.from(e)))
      .toList(growable: false);
});

/// Performance + training actions.
class PerformanceActions {
  PerformanceActions(this._api);

  final ApiClient _api;

  /// `POST /performance/self-assessment` -> 201 (FR-EMP-101).
  Future<void> submitSelfAssessment({
    required String cycleId,
    required String content,
    String? evidence,
  }) async {
    await _api.postMap(
      '/performance/self-assessment',
      data: {
        'cycleId': cycleId,
        'content': content,
        if (evidence != null && evidence.isNotEmpty) 'evidence': evidence,
      },
    );
  }

  /// `POST /performance/reviews/{id}/acknowledge` -> 204 (FR-EMP-102).
  Future<void> acknowledgeReview(String id) async {
    await _api.postEmpty('/performance/reviews/$id/acknowledge');
  }

  /// `POST /training/courses/{id}/enroll` -> 201 (FR-EMP-104).
  Future<void> enroll(String courseId) async {
    await _api.postEmpty('/training/courses/$courseId/enroll');
  }

  /// `POST /training/courses/{id}/complete` -> 200 (optional courses).
  Future<void> complete(String courseId, {num? score}) async {
    await _api.postMap(
      '/training/courses/$courseId/complete',
      data: {if (score != null) 'score': score},
    );
  }
}

final performanceActionsProvider = Provider<PerformanceActions>(
  (ref) => PerformanceActions(ref.watch(apiClientProvider)),
);
