import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../models/common.dart';
import 'api_provider.dart';

class AdminUsersQuery {
  const AdminUsersQuery({this.query = '', this.status, this.role});

  final String query;
  final String? status;
  final String? role;

  @override
  bool operator ==(Object other) =>
      other is AdminUsersQuery &&
      other.query == query &&
      other.status == status &&
      other.role == role;

  @override
  int get hashCode => Object.hash(query, status, role);
}

class AdminUserEntry {
  const AdminUserEntry({
    required this.id,
    required this.displayName,
    required this.employeeNumber,
    required this.status,
    required this.roles,
    required this.mfaEnabled,
    required this.sessionsActive,
    this.employeeId,
    this.workEmail,
    this.lastLoginAt,
  });

  factory AdminUserEntry.fromJson(Map<String, dynamic> json) => AdminUserEntry(
    id: JsonX.strOr(json['id']),
    employeeId: JsonX.str(json['employeeId']),
    employeeNumber: JsonX.strOr(json['employeeNumber']),
    displayName: JsonX.strOr(json['displayName']),
    workEmail: JsonX.str(json['workEmail']),
    roles: JsonX.stringList(json['roles']),
    status: JsonX.strOr(json['status']),
    mfaEnabled: JsonX.boolOr(json['mfaEnabled']),
    lastLoginAt: JsonX.str(json['lastLoginAt']),
    sessionsActive: JsonX.intOrNull(json['sessionsActive']) ?? 0,
  );

  final String id;
  final String? employeeId;
  final String employeeNumber;
  final String displayName;
  final String? workEmail;
  final List<String> roles;
  final String status;
  final bool mfaEnabled;
  final String? lastLoginAt;
  final int sessionsActive;
}

class AdminUsersPage {
  const AdminUsersPage({required this.items, required this.total});

  factory AdminUsersPage.fromJson(Map<String, dynamic> json) {
    final rawItems = json['items'];
    return AdminUsersPage(
      items: (rawItems is List ? rawItems : const [])
          .whereType<Map>()
          .map(
            (item) => AdminUserEntry.fromJson(Map<String, dynamic>.from(item)),
          )
          .toList(growable: false),
      total: JsonX.intOrNull(json['total']) ?? 0,
    );
  }

  final List<AdminUserEntry> items;
  final int total;
}

class AdminRoleEntry {
  const AdminRoleEntry({
    required this.name,
    required this.description,
    required this.isSystem,
    required this.userCount,
    required this.permissions,
  });

  factory AdminRoleEntry.fromJson(Map<String, dynamic> json) => AdminRoleEntry(
    name: JsonX.strOr(json['name']),
    description: JsonX.strOr(json['description']),
    isSystem: JsonX.boolOr(json['isSystem']),
    userCount: JsonX.intOrNull(json['userCount']) ?? 0,
    permissions: JsonX.stringList(json['permissions']),
  );

  final String name;
  final String description;
  final bool isSystem;
  final int userCount;
  final List<String> permissions;
}

class AdminConfigurationEntry {
  const AdminConfigurationEntry({
    required this.key,
    required this.value,
    required this.type,
    required this.description,
    this.updatedAt,
    this.updatedByName,
  });

  factory AdminConfigurationEntry.fromJson(Map<String, dynamic> json) =>
      AdminConfigurationEntry(
        key: JsonX.strOr(json['key']),
        value: JsonX.strOr(json['value']),
        type: JsonX.strOr(json['type']),
        description: JsonX.strOr(json['description']),
        updatedAt: JsonX.str(json['updatedAt']),
        updatedByName: JsonX.str(json['updatedByName']),
      );

  final String key;
  final String value;
  final String type;
  final String description;
  final String? updatedAt;
  final String? updatedByName;
}

class AdminAuditEntry {
  const AdminAuditEntry({
    required this.id,
    required this.actorName,
    required this.action,
    required this.outcome,
    required this.timestamp,
    this.actorEmployeeNumber,
    this.entityType,
    this.entityId,
    this.correlationId,
    this.ipAddress,
    this.detailsJson,
  });

  factory AdminAuditEntry.fromJson(Map<String, dynamic> json) =>
      AdminAuditEntry(
        id: JsonX.strOr(json['id']),
        actorName: JsonX.strOr(json['actorName']),
        actorEmployeeNumber: JsonX.str(json['actorEmployeeNumber']),
        action: JsonX.strOr(json['action']),
        entityType: JsonX.str(json['entityType']),
        entityId: JsonX.str(json['entityId']),
        outcome: JsonX.strOr(json['outcome']),
        timestamp: JsonX.strOr(json['timestamp']),
        correlationId: JsonX.str(json['correlationId']),
        ipAddress: JsonX.str(json['ipAddress']),
        detailsJson: JsonX.str(json['detailsJson']),
      );

  final String id;
  final String actorName;
  final String? actorEmployeeNumber;
  final String action;
  final String? entityType;
  final String? entityId;
  final String outcome;
  final String timestamp;
  final String? correlationId;
  final String? ipAddress;
  final String? detailsJson;
}

class AdminAuditPage {
  const AdminAuditPage({required this.items, required this.total});

  factory AdminAuditPage.fromJson(Map<String, dynamic> json) {
    final rawItems = json['items'];
    return AdminAuditPage(
      items: (rawItems is List ? rawItems : const [])
          .whereType<Map>()
          .map(
            (item) => AdminAuditEntry.fromJson(Map<String, dynamic>.from(item)),
          )
          .toList(growable: false),
      total: JsonX.intOrNull(json['total']) ?? 0,
    );
  }

  final List<AdminAuditEntry> items;
  final int total;
}

class AdminPayrollPeriod {
  const AdminPayrollPeriod({
    required this.id,
    required this.label,
    required this.status,
    required this.startDate,
    required this.endDate,
    this.cutOffDate,
    this.lockedAt,
  });

  factory AdminPayrollPeriod.fromJson(Map<String, dynamic> json) =>
      AdminPayrollPeriod(
        id: JsonX.strOr(json['id']),
        label: JsonX.strOr(json['label']),
        status: JsonX.strOr(json['status']),
        startDate: JsonX.strOr(json['startDate']),
        endDate: JsonX.strOr(json['endDate']),
        cutOffDate: JsonX.str(json['cutOffDate']),
        lockedAt: JsonX.str(json['lockedAt']),
      );

  final String id;
  final String label;
  final String status;
  final String startDate;
  final String endDate;
  final String? cutOffDate;
  final String? lockedAt;
}

class AdminReportQuery {
  const AdminReportQuery({
    required this.report,
    required this.from,
    required this.to,
    this.departmentId,
    this.locationId,
  });

  final String report;
  final String from;
  final String to;
  final String? departmentId;
  final String? locationId;

  @override
  bool operator ==(Object other) =>
      other is AdminReportQuery &&
      other.report == report &&
      other.from == from &&
      other.to == to &&
      other.departmentId == departmentId &&
      other.locationId == locationId;

  @override
  int get hashCode => Object.hash(report, from, to, departmentId, locationId);
}

final adminUsersProvider =
    FutureProvider.family<AdminUsersPage, AdminUsersQuery>((ref, filter) async {
      final query = <String, dynamic>{'page': 1, 'pageSize': 50};
      if (filter.query.trim().isNotEmpty) query['q'] = filter.query.trim();
      if (filter.status != null) query['status'] = filter.status;
      if (filter.role != null) query['role'] = filter.role;
      return AdminUsersPage.fromJson(
        await ref.watch(apiClientProvider).getMap('/admin/users', query: query),
      );
    });

final adminRolesProvider = FutureProvider<List<AdminRoleEntry>>((ref) async {
  final list = await ref.watch(apiClientProvider).getList('/admin/roles');
  return list
      .whereType<Map>()
      .map((item) => AdminRoleEntry.fromJson(Map<String, dynamic>.from(item)))
      .toList(growable: false);
});

final adminConfigurationProvider =
    FutureProvider<List<AdminConfigurationEntry>>((ref) async {
      final list = await ref
          .watch(apiClientProvider)
          .getList('/admin/configuration');
      return list
          .whereType<Map>()
          .map(
            (item) => AdminConfigurationEntry.fromJson(
              Map<String, dynamic>.from(item),
            ),
          )
          .toList(growable: false);
    });

final adminAuditProvider = FutureProvider<AdminAuditPage>((ref) async {
  final json = await ref
      .watch(apiClientProvider)
      .getMap('/admin/audit', query: {'page': 1, 'pageSize': 50});
  return AdminAuditPage.fromJson(json);
});

final adminOperationsProvider = FutureProvider<Map<String, dynamic>>(
  (ref) => ref.watch(apiClientProvider).getMap('/admin/operations'),
);

final adminPayrollPeriodsProvider = FutureProvider<List<AdminPayrollPeriod>>((
  ref,
) async {
  final list = await ref.watch(apiClientProvider).getList('/payroll-periods');
  return list
      .whereType<Map>()
      .map(
        (item) => AdminPayrollPeriod.fromJson(Map<String, dynamic>.from(item)),
      )
      .toList(growable: false);
});

final adminReportProvider =
    FutureProvider.family<Map<String, dynamic>, AdminReportQuery>((
      ref,
      query,
    ) async {
      final filters = <String, dynamic>{'from': query.from, 'to': query.to};
      if (query.departmentId != null)
        filters['departmentId'] = query.departmentId;
      if (query.locationId != null) filters['locationId'] = query.locationId;
      return ref
          .watch(apiClientProvider)
          .getMap('/reports/${query.report}', query: filters);
    });

class AdminUserActions {
  const AdminUserActions(this._ref);

  final Ref _ref;

  Future<void> updateRoles(String id, List<String> roles) async {
    await _ref
        .read(apiClientProvider)
        .putEmpty('/admin/users/$id/roles', data: {'roles': roles});
  }

  Future<void> suspend(String id, {String? reason}) async {
    await _ref
        .read(apiClientProvider)
        .postEmpty(
          '/admin/users/$id/suspend',
          data: {'reason': reason ?? 'Suspended by administrator'},
        );
  }

  Future<void> activate(String id, {String? reason}) async {
    await _ref
        .read(apiClientProvider)
        .postEmpty(
          '/admin/users/$id/activate',
          data: {'reason': reason ?? 'Reactivated by administrator'},
        );
  }

  Future<void> revokeSessions(String id) async => _ref
      .read(apiClientProvider)
      .postEmpty('/admin/users/$id/revoke-sessions');

  Future<void> updateConfiguration(String key, String value) async {
    await _ref
        .read(apiClientProvider)
        .putMap(
          '/admin/configuration/${Uri.encodeComponent(key)}',
          data: {'value': value},
        );
  }

  Future<void> updateRolePermissions(
    String name,
    List<String> permissions,
  ) async {
    await _ref
        .read(apiClientProvider)
        .putEmpty(
          '/admin/roles/${Uri.encodeComponent(name)}/permissions',
          data: {'permissions': permissions},
        );
  }

  Future<void> lockPayrollPeriod(String id, String reason) async {
    await _ref
        .read(apiClientProvider)
        .postMap('/payroll-periods/$id/lock', data: {'reason': reason});
  }

  Future<void> unlockPayrollPeriod(String id, String reason) async {
    await _ref
        .read(apiClientProvider)
        .postMap('/payroll-periods/$id/unlock', data: {'reason': reason});
  }
}

final adminUserActionsProvider = Provider<AdminUserActions>(
  (ref) => AdminUserActions(ref),
);
