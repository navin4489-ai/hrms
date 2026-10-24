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

final adminUsersProvider =
    FutureProvider.family<AdminUsersPage, AdminUsersQuery>((ref, filter) async {
      final api = ref.watch(apiClientProvider);
      final query = <String, dynamic>{'page': 1, 'pageSize': 50};
      if (filter.query.trim().isNotEmpty) query['q'] = filter.query.trim();
      if (filter.status != null) query['status'] = filter.status;
      if (filter.role != null) query['role'] = filter.role;
      return AdminUsersPage.fromJson(
        await api.getMap('/admin/users', query: query),
      );
    });

final adminRolesProvider = FutureProvider<List<AdminRoleEntry>>((ref) async {
  final list = await ref.watch(apiClientProvider).getList('/admin/roles');
  return list
      .whereType<Map>()
      .map((item) => AdminRoleEntry.fromJson(Map<String, dynamic>.from(item)))
      .toList(growable: false);
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

  Future<void> revokeSessions(String id) async {
    await _ref
        .read(apiClientProvider)
        .postEmpty('/admin/users/$id/revoke-sessions');
  }
}

final adminUserActionsProvider = Provider<AdminUserActions>(
  (ref) => AdminUserActions(ref),
);
