import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../core/network/api_client.dart';
import '../models/leave_models.dart';
import '../models/me_models.dart';
import 'api_provider.dart';

/// `GET /leave-types`.
final leaveTypesProvider = FutureProvider<List<LeaveType>>((ref) async {
  final api = ref.watch(apiClientProvider);
  final list = await api.getList('/leave-types');
  return list
      .whereType<Map>()
      .map((e) => LeaveType.fromJson(Map<String, dynamic>.from(e)))
      .toList(growable: false);
});

/// `GET /leave-balances?year=YYYY` (FR-EMP-040).
/// Family key: year.
final leaveBalancesProvider = FutureProvider.family<List<LeaveBalance>, int>((
  ref,
  year,
) async {
  final api = ref.watch(apiClientProvider);
  final list = await api.getList('/leave-balances', query: {'year': year});
  return list
      .whereType<Map>()
      .map((e) => LeaveBalance.fromJson(Map<String, dynamic>.from(e)))
      .toList(growable: false);
});

/// `GET /leave-policies`.
final leavePoliciesProvider = FutureProvider<List<LeavePolicy>>((ref) async {
  final api = ref.watch(apiClientProvider);
  final list = await api.getList('/leave-policies');
  return list
      .whereType<Map>()
      .map((e) => LeavePolicy.fromJson(Map<String, dynamic>.from(e)))
      .toList(growable: false);
});

/// Query object for `GET /leave-requests` filters.
class LeaveRequestQuery {
  const LeaveRequestQuery({this.status, this.year, this.team = false});

  final String? status;
  final int? year;
  final bool team;

  @override
  bool operator ==(Object other) =>
      other is LeaveRequestQuery &&
      other.status == status &&
      other.year == year &&
      other.team == team;

  @override
  int get hashCode => Object.hash(status, year, team);
}

/// `GET /leave-requests?status=&year=&team=true` (FR-EMP-043).
final leaveRequestsProvider =
    FutureProvider.family<List<LeaveRequest>, LeaveRequestQuery>((
      ref,
      query,
    ) async {
      final api = ref.watch(apiClientProvider);
      final params = <String, dynamic>{};
      if (query.status != null && query.status != 'All') {
        params['status'] = query.status;
      }
      if (query.year != null) params['year'] = query.year;
      if (query.team) params['team'] = 'true';
      final json = await api.getMap('/leave-requests', query: params);
      final items = json['items'] as List? ?? const [];
      return items
          .whereType<Map>()
          .map((e) => LeaveRequest.fromJson(Map<String, dynamic>.from(e)))
          .toList(growable: false);
    });

/// `GET /leave-requests/{id}` detail with decision history.
final leaveRequestDetailProvider =
    FutureProvider.family<LeaveRequestDetail, String>((ref, id) async {
      final api = ref.watch(apiClientProvider);
      final json = await api.getMap('/leave-requests/$id');
      return LeaveRequestDetail.fromJson(json);
    });

/// `GET /leave/calendar?from=&to=` (FR-EMP-045).
/// Family key: `'YYYY-MM-DD|YYYY-MM-DD'`.
final leaveCalendarProvider =
    FutureProvider.family<List<LeaveCalendarEntry>, String>((ref, range) async {
      final api = ref.watch(apiClientProvider);
      final parts = range.split('|');
      final list = await api.getList(
        '/leave/calendar',
        query: {'from': parts[0], 'to': parts[1]},
      );
      return list
          .whereType<Map>()
          .map((e) => LeaveCalendarEntry.fromJson(Map<String, dynamic>.from(e)))
          .toList(growable: false);
    });

/// Leave domain actions.
class LeaveActions {
  LeaveActions(this._api);

  final ApiClient _api;

  /// `POST /leave-requests` -> 201 (FR-EMP-041/042).
  Future<LeaveRequest> submitRequest({
    required String leaveTypeId,
    required String startDate,
    required String endDate,
    required String dayType,
    required String reason,
    String? attachmentUrl,
  }) async {
    final json = await _api.postMap(
      '/leave-requests',
      data: {
        'leaveTypeId': leaveTypeId,
        'startDate': startDate,
        'endDate': endDate,
        'dayType': dayType,
        'reason': reason,
        'attachmentUrl': attachmentUrl,
      },
    );
    return LeaveRequest.fromJson(json);
  }

  /// `POST /leave-requests/{id}/cancel` (FR-EMP-044).
  Future<void> cancelRequest({
    required String id,
    required String reason,
  }) async {
    await _api.postMap('/leave-requests/$id/cancel', data: {'reason': reason});
  }

  /// `POST /admin/leave/balances/adjust` — HR/admin credits or sets an
  /// employee's leave balance (FR-ADM-054).
  Future<void> adjustBalance({
    required String employeeId,
    required String leaveTypeId,
    required int year,
    required double amount,
    String mode = 'Add',
    String? note,
  }) async {
    await _api.postMap(
      '/admin/leave/balances/adjust',
      data: {
        'employeeId': employeeId,
        'leaveTypeId': leaveTypeId,
        'year': year,
        'amount': amount,
        'mode': mode,
        if (note != null) 'note': note,
      },
    );
  }
}

final leaveActionsProvider = Provider<LeaveActions>(
  (ref) => LeaveActions(ref.watch(apiClientProvider)),
);
