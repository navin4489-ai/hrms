import 'common.dart';

/// `GET /me` profile payload.
class MeProfile {
  const MeProfile({
    required this.employeeId,
    required this.employeeNumber,
    required this.displayName,
    this.preferredName,
    this.workEmail,
    this.personalEmail,
    this.phone,
    this.dateOfBirth,
    this.gender,
    this.address,
    this.emergencyContactName,
    this.emergencyContactPhone,
    this.designation,
    this.department,
    this.departmentId,
    this.location,
    this.locationId,
    this.managerName,
    this.managerEmployeeId,
    this.joiningDate,
    this.employmentType,
    this.employmentStatus,
    this.probationEndDate,
    this.legalEntity,
    this.costCentre,
    this.avatarUrl,
    this.status,
  });

  factory MeProfile.fromJson(Map<String, dynamic> json) => MeProfile(
    employeeId: JsonX.strOr(json['employeeId']),
    employeeNumber: JsonX.strOr(json['employeeNumber']),
    displayName: JsonX.strOr(json['displayName']),
    preferredName: JsonX.str(json['preferredName']),
    workEmail: JsonX.str(json['workEmail']),
    personalEmail: JsonX.str(json['personalEmail']),
    phone: JsonX.str(json['phone']),
    dateOfBirth: JsonX.str(json['dateOfBirth']),
    gender: JsonX.str(json['gender']),
    address: JsonX.str(json['address']),
    emergencyContactName: JsonX.str(json['emergencyContactName']),
    emergencyContactPhone: JsonX.str(json['emergencyContactPhone']),
    designation: JsonX.str(json['designation']),
    department: JsonX.str(json['department']),
    departmentId: JsonX.str(json['departmentId']),
    location: JsonX.str(json['location']),
    locationId: JsonX.str(json['locationId']),
    managerName: JsonX.str(json['managerName']),
    managerEmployeeId: JsonX.str(json['managerEmployeeId']),
    joiningDate: JsonX.str(json['joiningDate']),
    employmentType: JsonX.str(json['employmentType']),
    employmentStatus: JsonX.str(json['employmentStatus'] ?? json['status']),
    probationEndDate: JsonX.str(json['probationEndDate']),
    legalEntity: JsonX.str(json['legalEntity']),
    costCentre: JsonX.str(json['costCentre']),
    avatarUrl: JsonX.str(json['avatarUrl']),
    status: JsonX.str(json['status']),
  );

  final String employeeId;
  final String employeeNumber;
  final String displayName;
  final String? preferredName;
  final String? workEmail;
  final String? personalEmail;
  final String? phone;
  final String? dateOfBirth;
  final String? gender;
  final String? address;
  final String? emergencyContactName;
  final String? emergencyContactPhone;
  final String? designation;
  final String? department;
  final String? departmentId;
  final String? location;
  final String? locationId;
  final String? managerName;
  final String? managerEmployeeId;
  final String? joiningDate;
  final String? employmentType;
  final String? employmentStatus;
  final String? probationEndDate;
  final String? legalEntity;
  final String? costCentre;
  final String? avatarUrl;
  final String? status;

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

/// Employee summary block inside `GET /me/dashboard`.
class DashboardEmployee {
  const DashboardEmployee({
    required this.employeeNumber,
    required this.displayName,
    this.designation,
    this.department,
    this.location,
    this.managerName,
    this.employmentStatus,
  });

  factory DashboardEmployee.fromJson(Map<String, dynamic> json) =>
      DashboardEmployee(
        employeeNumber: JsonX.strOr(json['employeeNumber']),
        displayName: JsonX.strOr(json['displayName']),
        designation: JsonX.str(json['designation']),
        department: JsonX.str(json['department']),
        location: JsonX.str(json['location']),
        managerName: JsonX.str(json['managerName']),
        employmentStatus: JsonX.str(json['employmentStatus']),
      );

  final String employeeNumber;
  final String displayName;
  final String? designation;
  final String? department;
  final String? location;
  final String? managerName;
  final String? employmentStatus;
}

/// Shift info attached to attendance payloads.
class ShiftInfo {
  const ShiftInfo({
    required this.name,
    this.startTime,
    this.endTime,
    this.graceMinutes,
  });

  factory ShiftInfo.fromJson(Map<String, dynamic> json) => ShiftInfo(
    name: JsonX.strOr(json['name'], 'Shift'),
    startTime: JsonX.str(json['startTime']),
    endTime: JsonX.str(json['endTime']),
    graceMinutes: JsonX.intOrNull(json['graceMinutes']),
  );

  final String name;
  final String? startTime;
  final String? endTime;
  final int? graceMinutes;
}

/// Today's attendance card in `GET /me/dashboard`.
class DashboardAttendance {
  const DashboardAttendance({
    required this.workDate,
    required this.status,
    this.shift,
    this.checkIn,
    this.checkOut,
    this.workedHours,
    this.canPunchIn = false,
    this.canPunchOut = false,
    this.lateByMinutes,
  });

  factory DashboardAttendance.fromJson(Map<String, dynamic> json) =>
      DashboardAttendance(
        workDate: JsonX.strOr(json['workDate']),
        status: JsonX.strOr(json['status'], 'NotMarked'),
        shift: json['shift'] is Map
            ? ShiftInfo.fromJson(
                Map<String, dynamic>.from(json['shift'] as Map),
              )
            : null,
        checkIn: JsonX.str(json['checkIn']),
        checkOut: JsonX.str(json['checkOut']),
        workedHours: JsonX.doubleOrNull(json['workedHours']),
        canPunchIn: JsonX.boolOr(json['canPunchIn']),
        canPunchOut: JsonX.boolOr(json['canPunchOut']),
        lateByMinutes: JsonX.intOrNull(json['lateByMinutes']),
      );

  final String workDate;
  final String status;
  final ShiftInfo? shift;
  final String? checkIn;
  final String? checkOut;
  final double? workedHours;
  final bool canPunchIn;
  final bool canPunchOut;
  final int? lateByMinutes;

  bool get canPunch => canPunchIn || canPunchOut;
}

/// `leaveBalances` entry on the dashboard.
class LeaveBalance {
  const LeaveBalance({
    required this.leaveTypeId,
    required this.code,
    required this.name,
    required this.available,
    this.used = 0,
    this.pending = 0,
    this.accrued = 0,
    this.carryForward = 0,
    this.unit = 'Days',
  });

  factory LeaveBalance.fromJson(Map<String, dynamic> json) => LeaveBalance(
    leaveTypeId: JsonX.strOr(json['leaveTypeId']),
    code: JsonX.strOr(json['code']),
    name: JsonX.strOr(json['name']),
    available: JsonX.doubleOrNull(json['available']) ?? 0,
    used: JsonX.doubleOrNull(json['used']) ?? 0,
    pending: JsonX.doubleOrNull(json['pending']) ?? 0,
    accrued: JsonX.doubleOrNull(json['accrued']) ?? 0,
    carryForward: JsonX.doubleOrNull(json['carryForward']) ?? 0,
    unit: JsonX.strOr(json['unit'], 'Days'),
  );

  final String leaveTypeId;
  final String code;
  final String name;
  final double available;
  final double used;
  final double pending;
  final double accrued;
  final double carryForward;
  final String unit;
}

/// `myRequests` entry on the dashboard.
class RequestSummaryItem {
  const RequestSummaryItem({
    required this.id,
    required this.type,
    required this.title,
    required this.status,
    this.date,
    this.amount,
    this.deepLink,
  });

  factory RequestSummaryItem.fromJson(Map<String, dynamic> json) =>
      RequestSummaryItem(
        id: JsonX.strOr(json['id']),
        type: JsonX.strOr(json['type']),
        title: JsonX.strOr(json['title']),
        status: JsonX.strOr(json['status']),
        date: JsonX.str(json['date']),
        amount: JsonX.doubleOrNull(json['amount']),
        deepLink: JsonX.str(json['deepLink']),
      );

  final String id;
  final String type;
  final String title;
  final String status;
  final String? date;
  final double? amount;
  final String? deepLink;
}

/// `announcements` entry on the dashboard (summary form).
class AnnouncementSummary {
  const AnnouncementSummary({
    required this.id,
    required this.title,
    this.summary,
    this.category,
    this.publishedAt,
    this.requiresAcknowledgement = false,
  });

  factory AnnouncementSummary.fromJson(Map<String, dynamic> json) =>
      AnnouncementSummary(
        id: JsonX.strOr(json['id']),
        title: JsonX.strOr(json['title']),
        summary: JsonX.str(json['summary']),
        category: JsonX.str(json['category']),
        publishedAt: JsonX.str(json['publishedAt']),
        requiresAcknowledgement: JsonX.boolOr(json['requiresAcknowledgement']),
      );

  final String id;
  final String title;
  final String? summary;
  final String? category;
  final String? publishedAt;
  final bool requiresAcknowledgement;
}

/// `holidays` entry on the dashboard.
class HolidayItem {
  const HolidayItem({required this.date, required this.name, this.type});

  factory HolidayItem.fromJson(Map<String, dynamic> json) => HolidayItem(
    date: JsonX.strOr(json['date']),
    name: JsonX.strOr(json['name']),
    type: JsonX.str(json['type']),
  );

  final String date;
  final String name;
  final String? type;
}

/// `GET /me/dashboard` payload (FR-EMP-010..014).
class DashboardData {
  const DashboardData({
    required this.greeting,
    required this.today,
    this.employee,
    this.attendance,
    this.leaveBalances = const [],
    this.pendingApprovals = 0,
    this.myRequests = const [],
    this.announcements = const [],
    this.holidays = const [],
    this.notificationsUnread = 0,
    this.quickActions = const [],
  });

  factory DashboardData.fromJson(Map<String, dynamic> json) => DashboardData(
    greeting: JsonX.strOr(json['greeting']),
    today: JsonX.strOr(json['today']),
    employee: json['employee'] is Map
        ? DashboardEmployee.fromJson(
            Map<String, dynamic>.from(json['employee'] as Map),
          )
        : null,
    attendance: json['attendance'] is Map
        ? DashboardAttendance.fromJson(
            Map<String, dynamic>.from(json['attendance'] as Map),
          )
        : null,
    leaveBalances: JsonX.mapList(json['leaveBalances'])
        .map(LeaveBalance.fromJson)
        .toList(growable: false),
    pendingApprovals: JsonX.intOrNull(json['pendingApprovals']) ?? 0,
    myRequests: JsonX.mapList(json['myRequests'])
        .map(RequestSummaryItem.fromJson)
        .toList(growable: false),
    announcements: JsonX.mapList(json['announcements'])
        .map(AnnouncementSummary.fromJson)
        .toList(growable: false),
    holidays: JsonX.mapList(json['holidays'])
        .map(HolidayItem.fromJson)
        .toList(growable: false),
    notificationsUnread: JsonX.intOrNull(json['notificationsUnread']) ?? 0,
    quickActions: JsonX.stringList(json['quickActions']),
  );

  final String greeting;
  final String today;
  final DashboardEmployee? employee;
  final DashboardAttendance? attendance;
  final List<LeaveBalance> leaveBalances;
  final int pendingApprovals;
  final List<RequestSummaryItem> myRequests;
  final List<AnnouncementSummary> announcements;
  final List<HolidayItem> holidays;
  final int notificationsUnread;
  final List<String> quickActions;
}

/// `GET /me/employment-history` item.
class EmploymentHistoryEntry {
  const EmploymentHistoryEntry({
    required this.eventType,
    required this.effectiveDate,
    this.oldValue,
    this.newValue,
    this.reason,
    this.approvedBy,
  });

  factory EmploymentHistoryEntry.fromJson(Map<String, dynamic> json) =>
      EmploymentHistoryEntry(
        eventType: JsonX.strOr(json['eventType']),
        effectiveDate: JsonX.strOr(json['effectiveDate']),
        oldValue: JsonX.str(json['oldValue']),
        newValue: JsonX.str(json['newValue']),
        reason: JsonX.str(json['reason']),
        approvedBy: JsonX.str(json['approvedBy']),
      );

  final String eventType;
  final String effectiveDate;
  final String? oldValue;
  final String? newValue;
  final String? reason;
  final String? approvedBy;
}

/// `GET /me/notification-preferences` item (FR-EMP-121).
class NotificationPreference {
  const NotificationPreference({
    required this.category,
    this.inApp = true,
    this.push = true,
    this.email = false,
    this.mandatory = false,
  });

  factory NotificationPreference.fromJson(Map<String, dynamic> json) =>
      NotificationPreference(
        category: JsonX.strOr(json['category']),
        inApp: JsonX.boolOr(json['inApp'], true),
        push: JsonX.boolOr(json['push'], true),
        email: JsonX.boolOr(json['email']),
        mandatory: JsonX.boolOr(json['mandatory']),
      );

  Map<String, dynamic> toJson() => {
    'category': category,
    'inApp': inApp,
    'push': push,
    'email': email,
    'mandatory': mandatory,
  };

  NotificationPreference copyWith({bool? inApp, bool? push, bool? email}) =>
      NotificationPreference(
        category: category,
        inApp: inApp ?? this.inApp,
        push: push ?? this.push,
        email: email ?? this.email,
        mandatory: mandatory,
      );

  final String category;
  final bool inApp;
  final bool push;
  final bool email;
  final bool mandatory;
}
