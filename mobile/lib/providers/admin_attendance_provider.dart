import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'api_provider.dart';

/// Filter for the admin attendance dashboard + punch-event viewer.
class AdminAttendanceQuery {
  const AdminAttendanceQuery({
    required this.from,
    required this.to,
    this.departmentId,
    this.locationId,
  });

  final String from;
  final String to;
  final String? departmentId;
  final String? locationId;

  @override
  bool operator ==(Object other) =>
      other is AdminAttendanceQuery &&
      other.from == from &&
      other.to == to &&
      other.departmentId == departmentId &&
      other.locationId == locationId;

  @override
  int get hashCode => Object.hash(from, to, departmentId, locationId);
}

/// `GET /admin/attendance/dashboard` — attendance + leave analytics.
final adminAttendanceDashboardProvider =
    FutureProvider.family<Map<String, dynamic>, AdminAttendanceQuery>((
      ref,
      query,
    ) async {
      final params = <String, dynamic>{'from': query.from, 'to': query.to};
      if (query.departmentId != null) {
        params['departmentId'] = query.departmentId;
      }
      if (query.locationId != null) {
        params['locationId'] = query.locationId;
      }
      return ref
          .watch(apiClientProvider)
          .getMap('/admin/attendance/dashboard', query: params);
    });

/// `GET /admin/attendance/punch-events` — captured location/selfie/timestamp.
final adminPunchEventsProvider =
    FutureProvider.family<Map<String, dynamic>, AdminAttendanceQuery>((
      ref,
      query,
    ) async {
      final params = <String, dynamic>{
        'from': query.from,
        'to': query.to,
        'page': 1,
        'pageSize': 100,
      };
      if (query.departmentId != null) {
        params['departmentId'] = query.departmentId;
      }
      if (query.locationId != null) {
        params['locationId'] = query.locationId;
      }
      return ref
          .watch(apiClientProvider)
          .getMap('/admin/attendance/punch-events', query: params);
    });
