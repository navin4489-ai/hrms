import 'common.dart';

/// `GET /employees/directory` item — privacy-aware, minimal fields
/// (FR-EMP-024).
class DirectoryEntry {
  const DirectoryEntry({
    required this.employeeId,
    required this.employeeNumber,
    required this.displayName,
    this.designation,
    this.department,
    this.location,
    this.workEmail,
    this.hasPhone = false,
    this.avatarUrl,
  });

  factory DirectoryEntry.fromJson(Map<String, dynamic> json) => DirectoryEntry(
    employeeId: JsonX.strOr(json['employeeId']),
    employeeNumber: JsonX.strOr(json['employeeNumber']),
    displayName: JsonX.strOr(json['displayName']),
    designation: JsonX.str(json['designation']),
    department: JsonX.str(json['department']),
    location: JsonX.str(json['location']),
    workEmail: JsonX.str(json['workEmail']),
    hasPhone: JsonX.boolOr(json['hasPhone']),
    avatarUrl: JsonX.str(json['avatarUrl']),
  );

  final String employeeId;
  final String employeeNumber;
  final String displayName;
  final String? designation;
  final String? department;
  final String? location;
  final String? workEmail;
  final bool hasPhone;
  final String? avatarUrl;

  String get initials {
    final parts = displayName
        .trim()
        .split(RegExp(r'\s+'))
        .where((p) => p.isNotEmpty);
    if (parts.isEmpty) return '?';
    if (parts.length == 1) {
      return parts.first.substring(0, 1).toUpperCase();
    }
    return (parts.first.substring(0, 1) + parts.last.substring(0, 1))
        .toUpperCase();
  }
}

/// `GET /org-units` item (used for department filters).
class OrgUnit {
  const OrgUnit({
    required this.id,
    required this.name,
    this.code,
    this.type,
    this.status,
    this.parentId,
    this.managerId,
    this.managerName,
    this.effectiveFrom,
    this.effectiveTo,
  });

  factory OrgUnit.fromJson(Map<String, dynamic> json) => OrgUnit(
    id: JsonX.strOr(json['id']),
    name: JsonX.strOr(json['name']),
    code: JsonX.str(json['code']),
    type: JsonX.str(json['type']),
    status: JsonX.str(json['status']),
    parentId: JsonX.str(json['parentId']),
    managerId: JsonX.str(json['managerId']),
    managerName: JsonX.str(json['managerName']),
    effectiveFrom: JsonX.str(json['effectiveFrom']),
    effectiveTo: JsonX.str(json['effectiveTo']),
  );

  final String id;
  final String name;
  final String? code;
  final String? type;
  final String? status;
  final String? parentId;
  final String? managerId;
  final String? managerName;
  final String? effectiveFrom;
  final String? effectiveTo;

  bool get isLegalEntity => type == 'Entity' || type == 'LegalEntity';
}

/// `GET /locations` item (used for location filters).
class LocationEntry {
  const LocationEntry({
    required this.id,
    required this.name,
    this.city,
    this.code,
    this.country,
    this.timeZone,
    this.status,
  });

  factory LocationEntry.fromJson(Map<String, dynamic> json) => LocationEntry(
    id: JsonX.strOr(json['id']),
    name: JsonX.strOr(json['name']),
    city: JsonX.str(json['city']),
    code: JsonX.str(json['code']),
    country: JsonX.str(json['country']),
    timeZone: JsonX.str(json['timeZone']),
    status: JsonX.str(json['status']),
  );

  final String id;
  final String name;
  final String? city;
  final String? code;
  final String? country;
  final String? timeZone;
  final String? status;
}

/// `GET /designations` item used by the create-employee form.
class DesignationEntry {
  const DesignationEntry({
    required this.id,
    required this.name,
    this.code,
    this.grade,
    this.status,
  });

  factory DesignationEntry.fromJson(Map<String, dynamic> json) =>
      DesignationEntry(
        id: JsonX.strOr(json['id']),
        name: JsonX.strOr(json['name']),
        code: JsonX.str(json['code']),
        grade: JsonX.str(json['grade']),
        status: JsonX.str(json['status']),
      );

  final String id;
  final String name;
  final String? code;
  final String? grade;
  final String? status;
}

/// Employee option for reporting-manager pickers.
class EmployeeManagerOption {
  const EmployeeManagerOption({
    required this.employeeId,
    required this.employeeNumber,
    required this.displayName,
    this.status,
  });

  factory EmployeeManagerOption.fromJson(Map<String, dynamic> json) =>
      EmployeeManagerOption(
        employeeId: JsonX.strOr(json['employeeId']),
        employeeNumber: JsonX.strOr(json['employeeNumber']),
        displayName: JsonX.strOr(json['displayName']),
        status: JsonX.str(json['status']),
      );

  final String employeeId;
  final String employeeNumber;
  final String displayName;
  final String? status;

  String get label => '$displayName · $employeeNumber';
}
