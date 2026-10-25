import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../models/common.dart';
import 'api_provider.dart';

/// One per-org module flag from `GET /me/modules` or an org detail.
class OrgModuleFlag {
  const OrgModuleFlag({
    required this.key,
    required this.label,
    required this.enabled,
  });

  factory OrgModuleFlag.fromJson(Map<String, dynamic> json) => OrgModuleFlag(
    key: JsonX.strOr(json['key']),
    label: JsonX.strOr(json['label']),
    enabled: JsonX.boolOr(json['enabled']),
  );

  final String key;
  final String label;
  final bool enabled;
}

/// `GET /admin/organizations` list item.
class OrganizationSummary {
  const OrganizationSummary({
    required this.id,
    required this.name,
    required this.code,
    this.country = 'IN',
    this.timeZone = 'Asia/Kolkata',
    this.currency = 'INR',
    this.status = 'Active',
    this.employeeCount = 0,
  });

  factory OrganizationSummary.fromJson(Map<String, dynamic> json) =>
      OrganizationSummary(
        id: JsonX.strOr(json['id']),
        name: JsonX.strOr(json['name']),
        code: JsonX.strOr(json['code']),
        country: JsonX.strOr(json['country'], 'IN'),
        timeZone: JsonX.strOr(json['timeZone'], 'Asia/Kolkata'),
        currency: JsonX.strOr(json['currency'], 'INR'),
        status: JsonX.strOr(json['status'], 'Active'),
        employeeCount: JsonX.intOrNull(json['employeeCount']) ?? 0,
      );

  final String id;
  final String name;
  final String code;
  final String country;
  final String timeZone;
  final String currency;
  final String status;
  final int employeeCount;
}

/// `GET /admin/organizations/{id}` detail with module flags.
class OrganizationDetail {
  const OrganizationDetail({
    required this.organization,
    this.modules = const [],
    this.employeeCodePrefix = '',
    this.employeeCodePadding = 3,
    this.employeeCodeNext = 1,
  });

  factory OrganizationDetail.fromJson(Map<String, dynamic> json) =>
      OrganizationDetail(
        organization: OrganizationSummary.fromJson(
          Map<String, dynamic>.from(json['organization'] as Map? ?? const {}),
        ),
        modules: JsonX.mapList(json['modules'])
            .map(OrgModuleFlag.fromJson)
            .toList(growable: false),
        employeeCodePrefix: JsonX.strOr(json['employeeCodePrefix']),
        employeeCodePadding: JsonX.intOrNull(json['employeeCodePadding']) ?? 3,
        employeeCodeNext: JsonX.intOrNull(json['employeeCodeNext']) ?? 1,
      );

  final OrganizationSummary organization;
  final List<OrgModuleFlag> modules;
  final String employeeCodePrefix;
  final int employeeCodePadding;
  final int employeeCodeNext;
}

/// `GET /me/modules` — enabled feature modules for the caller's organization.
/// Used by the app to hide modules a SuperAdmin disabled for this org.
final meModulesProvider = FutureProvider<Set<String>>((ref) async {
  final api = ref.watch(apiClientProvider);
  final list = await api.getList('/me/modules');
  return list
      .whereType<Map>()
      .map((e) => OrgModuleFlag.fromJson(Map<String, dynamic>.from(e)))
      .where((m) => m.enabled)
      .map((m) => m.key)
      .toSet();
});

/// `GET /admin/organizations` (SuperAdmin only).
final organizationsProvider = FutureProvider<List<OrganizationSummary>>((
  ref,
) async {
  final api = ref.watch(apiClientProvider);
  final list = await api.getList('/admin/organizations');
  return list
      .whereType<Map>()
      .map((e) => OrganizationSummary.fromJson(Map<String, dynamic>.from(e)))
      .toList(growable: false);
});

/// `GET /admin/organizations/{id}` (SuperAdmin only).
final organizationDetailProvider =
    FutureProvider.family<OrganizationDetail, String>((ref, id) async {
      final api = ref.watch(apiClientProvider);
      final json = await api.getMap('/admin/organizations/$id');
      return OrganizationDetail.fromJson(json);
    });

/// Create/update organizations and toggle per-org modules (SuperAdmin only).
class OrganizationActions {
  OrganizationActions(this._ref);

  final Ref _ref;

  Future<OrganizationDetail> create({
    required String name,
    required String code,
    String country = 'IN',
    String timeZone = 'Asia/Kolkata',
    String currency = 'INR',
    String? employeeCodePrefix,
    int employeeCodePadding = 3,
  }) async {
    final api = _ref.read(apiClientProvider);
    final json = await api.postMap(
      '/admin/organizations',
      data: {
        'name': name,
        'code': code,
        'country': country,
        'timeZone': timeZone,
        'currency': currency,
        if (employeeCodePrefix != null && employeeCodePrefix.isNotEmpty)
          'employeeCodePrefix': employeeCodePrefix,
        'employeeCodePadding': employeeCodePadding,
      },
    );
    return OrganizationDetail.fromJson(json);
  }

  Future<OrganizationDetail> update({
    required String id,
    required String name,
    required String country,
    required String timeZone,
    required String currency,
    required String status,
  }) async {
    final api = _ref.read(apiClientProvider);
    final json = await api.putMap(
      '/admin/organizations/$id',
      data: {
        'name': name,
        'country': country,
        'timeZone': timeZone,
        'currency': currency,
        'status': status,
      },
    );
    return OrganizationDetail.fromJson(json);
  }

  Future<OrganizationDetail> setModules({
    required String id,
    required Map<String, bool> modules,
  }) async {
    final api = _ref.read(apiClientProvider);
    final json = await api.putMap(
      '/admin/organizations/$id/modules',
      data: {'modules': modules},
    );
    return OrganizationDetail.fromJson(json);
  }
}

final organizationActionsProvider = Provider<OrganizationActions>(
  (ref) => OrganizationActions(ref),
);
