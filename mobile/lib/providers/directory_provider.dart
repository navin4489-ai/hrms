import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../models/directory_models.dart';
import '../models/me_models.dart';
import 'api_provider.dart';

/// Filters for `GET /employees/directory`.
class DirectoryQuery {
  const DirectoryQuery({this.q = '', this.departmentId, this.locationId});

  final String q;
  final String? departmentId;
  final String? locationId;

  bool get isEmpty =>
      q.trim().isEmpty && departmentId == null && locationId == null;

  @override
  bool operator ==(Object other) =>
      other is DirectoryQuery &&
      other.q == q &&
      other.departmentId == departmentId &&
      other.locationId == locationId;

  @override
  int get hashCode => Object.hash(q, departmentId, locationId);
}

/// Directory page result.
class DirectoryPage {
  const DirectoryPage({
    required this.items,
    required this.page,
    required this.total,
    required this.hasNextPage,
  });

  factory DirectoryPage.fromJson(Map<String, dynamic> json) {
    final rawItems = json['items'];
    return DirectoryPage(
      items: (rawItems is List ? rawItems : const [])
          .whereType<Map>()
          .map((e) => DirectoryEntry.fromJson(Map<String, dynamic>.from(e)))
          .toList(growable: false),
      page: (json['page'] as num?)?.toInt() ?? 1,
      total: (json['total'] as num?)?.toInt() ?? 0,
      hasNextPage: json['hasNextPage'] as bool? ?? false,
    );
  }

  final List<DirectoryEntry> items;
  final int page;
  final int total;
  final bool hasNextPage;
}

/// `GET /employees/directory` (FR-EMP-024, privacy-aware).
final directoryProvider = FutureProvider.family<DirectoryPage, DirectoryQuery>((
  ref,
  query,
) async {
  final api = ref.watch(apiClientProvider);
  final params = <String, dynamic>{'page': 1, 'pageSize': 50};
  if (query.q.trim().isNotEmpty) params['q'] = query.q.trim();
  if (query.departmentId != null) params['departmentId'] = query.departmentId;
  if (query.locationId != null) params['locationId'] = query.locationId;
  final json = await api.getMap('/employees/directory', query: params);
  return DirectoryPage.fromJson(json);
});

/// `GET /org-units` — only Department-type entries used for filters.
final departmentsProvider = FutureProvider<List<OrgUnit>>((ref) async {
  final api = ref.watch(apiClientProvider);
  final list = await api.getList('/org-units');
  final units = list
      .whereType<Map>()
      .map((e) => OrgUnit.fromJson(Map<String, dynamic>.from(e)))
      .where((u) => u.type == 'Department' || u.type == null)
      .toList(growable: false);
  return units;
});

/// `GET /locations`.
final locationsProvider = FutureProvider<List<LocationEntry>>((ref) async {
  final api = ref.watch(apiClientProvider);
  final list = await api.getList('/locations');
  return list
      .whereType<Map>()
      .map((e) => LocationEntry.fromJson(Map<String, dynamic>.from(e)))
      .toList(growable: false);
});

/// Legal entities, departments, designations and locations required to create
/// an employee record.
class EmployeeCreateOptions {
  const EmployeeCreateOptions({
    required this.departments,
    required this.legalEntities,
    required this.locations,
    required this.designations,
  });

  final List<OrgUnit> departments;
  final List<OrgUnit> legalEntities;
  final List<LocationEntry> locations;
  final List<DesignationEntry> designations;
}

final employeeCreateOptionsProvider = FutureProvider<EmployeeCreateOptions>((
  ref,
) async {
  final api = ref.watch(apiClientProvider);
  final orgUnitsJson = await api.getList('/org-units');
  final locationJson = await api.getList('/locations');
  final designationJson = await api.getList('/designations');

  final orgUnits = orgUnitsJson
      .whereType<Map>()
      .map((e) => OrgUnit.fromJson(Map<String, dynamic>.from(e)))
      .where((unit) => unit.status == null || unit.status == 'Active')
      .toList(growable: false);
  final locations = locationJson
      .whereType<Map>()
      .map((e) => LocationEntry.fromJson(Map<String, dynamic>.from(e)))
      .toList(growable: false);
  final designations = designationJson
      .whereType<Map>()
      .map((e) => DesignationEntry.fromJson(Map<String, dynamic>.from(e)))
      .where((item) => item.status == null || item.status == 'Active')
      .toList(growable: false);

  return EmployeeCreateOptions(
    departments: orgUnits.where((unit) => unit.type == 'Department').toList(),
    legalEntities: orgUnits
        .where((unit) => unit.type == 'LegalEntity')
        .toList(),
    locations: locations,
    designations: designations,
  );
});

/// `GET /employees/{id}` — own record or `employee.view` for others.
final employeeDetailProvider = FutureProvider.family<MeProfile, String>((
  ref,
  id,
) async {
  final api = ref.watch(apiClientProvider);
  final json = await api.getMap('/employees/$id');
  return MeProfile.fromJson(json);
});
