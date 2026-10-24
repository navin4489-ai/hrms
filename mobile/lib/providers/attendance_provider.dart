import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../core/network/api_client.dart';
import '../models/attendance_models.dart';
import 'api_provider.dart';

/// One month of attendance including the month summary.
class MonthAttendance {
  const MonthAttendance({required this.records, required this.summary});

  final List<AttendanceRecord> records;
  final AttendanceMonthSummary summary;
}

/// `GET /attendance?month=YYYY-MM` (FR-EMP-030).
/// Family key: `YYYY-MM`.
final attendanceMonthProvider = FutureProvider.family<MonthAttendance, String>((
  ref,
  month,
) async {
  final api = ref.watch(apiClientProvider);
  final json = await api.getMap('/attendance', query: {'month': month});
  final items = (json['items'] as List? ?? const [])
      .whereType<Map>()
      .map((e) => AttendanceRecord.fromJson(Map<String, dynamic>.from(e)))
      .toList(growable: false);
  final summary = json['summary'] is Map
      ? AttendanceMonthSummary.fromJson(
          Map<String, dynamic>.from(json['summary'] as Map),
        )
      : const AttendanceMonthSummary();
  return MonthAttendance(records: items, summary: summary);
});

/// `GET /attendance/today` (FR-EMP-031 punch state on the attendance screen).
final attendanceTodayProvider = FutureProvider<AttendanceRecord>((ref) async {
  final api = ref.watch(apiClientProvider);
  final json = await api.getMap('/attendance/today');
  return AttendanceRecord.fromJson(json);
});

List<AttendanceCorrection> _correctionItems(Map<String, dynamic> json) {
  final raw = json['items'];
  final list = raw is List ? raw : const [];
  return list
      .whereType<Map>()
      .map((e) => AttendanceCorrection.fromJson(Map<String, dynamic>.from(e)))
      .toList(growable: false);
}

/// `GET /attendance-corrections?status=&month=` (FR-EMP-036).
/// Family key: status filter (`''` = all).
final correctionsProvider =
    FutureProvider.family<List<AttendanceCorrection>, String>((
      ref,
      status,
    ) async {
      final api = ref.watch(apiClientProvider);
      final query = <String, dynamic>{};
      if (status.isNotEmpty) query['status'] = status;
      final json = await api.getMap('/attendance-corrections', query: query);
      return _correctionItems(json);
    });

/// `GET /attendance-corrections/{id}` with decision history.
final correctionDetailProvider =
    FutureProvider.family<AttendanceCorrectionDetail, String>((ref, id) async {
      final api = ref.watch(apiClientProvider);
      final json = await api.getMap('/attendance-corrections/$id');
      return AttendanceCorrectionDetail.fromJson(json);
    });

/// Actions for the attendance domain.
class AttendanceActions {
  AttendanceActions(this._api);

  final ApiClient _api;

  /// `POST /attendance/punch` — location fields are omitted (no location
  /// package; see INTEGRATION_NOTES.md).
  Future<PunchResult> punch() async {
    final json = await _api.postMap(
      '/attendance/punch',
      data: {'source': 'Mobile'},
    );
    return PunchResult.fromJson(json);
  }

  /// `POST /attendance-corrections` -> 201.
  Future<AttendanceCorrection> submitCorrection({
    required String workDate,
    required String type,
    String? proposedCheckIn,
    String? proposedCheckOut,
    required String reason,
  }) async {
    final json = await _api.postMap(
      '/attendance-corrections',
      data: {
        'workDate': workDate,
        'type': type,
        'proposedCheckIn': proposedCheckIn,
        'proposedCheckOut': proposedCheckOut,
        'reason': reason,
        'attachmentUrl': null,
      },
    );
    return AttendanceCorrection.fromJson(json);
  }
}

final attendanceActionsProvider = Provider<AttendanceActions>(
  (ref) => AttendanceActions(ref.watch(apiClientProvider)),
);
