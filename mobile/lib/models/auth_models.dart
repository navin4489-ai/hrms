import 'common.dart';

/// The authenticated user principal returned by `/auth/login` and
/// `/auth/mfa/verify`.
class UserPrincipal {
  const UserPrincipal({
    required this.id,
    required this.employeeId,
    required this.employeeNumber,
    required this.displayName,
    this.workEmail,
    this.designation,
    this.department,
    this.roles = const [],
    this.permissions = const [],
  });

  factory UserPrincipal.fromJson(Map<String, dynamic> json) => UserPrincipal(
    id: JsonX.strOr(json['id']),
    employeeId: JsonX.strOr(json['employeeId']),
    employeeNumber: JsonX.strOr(json['employeeNumber']),
    displayName: JsonX.strOr(json['displayName']),
    workEmail: JsonX.str(json['workEmail']),
    designation: JsonX.str(json['designation']),
    department: JsonX.str(json['department']),
    roles: JsonX.stringList(json['roles']),
    permissions: JsonX.stringList(json['permissions']),
  );

  Map<String, dynamic> toJson() => {
    'id': id,
    'employeeId': employeeId,
    'employeeNumber': employeeNumber,
    'displayName': displayName,
    'workEmail': workEmail,
    'designation': designation,
    'department': department,
    'roles': roles,
    'permissions': permissions,
  };

  final String id;
  final String employeeId;
  final String employeeNumber;
  final String displayName;
  final String? workEmail;
  final String? designation;
  final String? department;
  final List<String> roles;
  final List<String> permissions;

  /// FR-EMP-014: UI gating helper. SuperAdmin-style wildcard is not used by
  /// the mobile app; the login payload lists explicit codes.
  bool hasPermission(String code) => permissions.contains(code);

  bool hasAnyPermission(Iterable<String> codes) =>
      codes.any(permissions.contains);

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

/// Result of `POST /auth/login` (and `/auth/mfa/verify`).
class AuthResult {
  const AuthResult({
    this.accessToken,
    this.refreshToken,
    this.expiresIn,
    this.mfaRequired = false,
    this.mfaToken,
    this.user,
  });

  factory AuthResult.fromJson(Map<String, dynamic> json) => AuthResult(
    accessToken: JsonX.str(json['accessToken']),
    refreshToken: JsonX.str(json['refreshToken']),
    expiresIn: JsonX.intOrNull(json['expiresIn']),
    mfaRequired: JsonX.boolOr(json['mfaRequired']),
    mfaToken: JsonX.str(json['mfaToken']),
    user: json['user'] is Map
        ? UserPrincipal.fromJson(Map<String, dynamic>.from(json['user'] as Map))
        : null,
  );

  final String? accessToken;
  final String? refreshToken;
  final int? expiresIn;
  final bool mfaRequired;
  final String? mfaToken;
  final UserPrincipal? user;

  bool get isComplete =>
      !mfaRequired &&
      accessToken != null &&
      refreshToken != null &&
      user != null;
}

/// `GET /auth/sessions` item.
class SessionInfo {
  const SessionInfo({
    required this.id,
    required this.deviceName,
    this.ipAddress,
    this.createdAt,
    this.lastUsedAt,
    this.current = false,
  });

  factory SessionInfo.fromJson(Map<String, dynamic> json) => SessionInfo(
    id: JsonX.strOr(json['id']),
    deviceName: JsonX.strOr(json['deviceName'], 'Unknown device'),
    ipAddress: JsonX.str(json['ipAddress']),
    createdAt: JsonX.str(json['createdAt']),
    lastUsedAt: JsonX.str(json['lastUsedAt']),
    current: JsonX.boolOr(json['current']),
  );

  final String id;
  final String deviceName;
  final String? ipAddress;
  final String? createdAt;
  final String? lastUsedAt;
  final bool current;
}
