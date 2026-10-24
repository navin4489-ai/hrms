import 'attendance_models.dart' show DecisionEntry;
import 'common.dart';

/// `GET /leave-types` item.
class LeaveType {
  const LeaveType({
    required this.id,
    required this.code,
    required this.name,
    this.isPaid = true,
    this.requiresAttachment = false,
    this.allowsHalfDay = false,
    this.maxConsecutiveDays,
  });

  factory LeaveType.fromJson(Map<String, dynamic> json) => LeaveType(
    id: JsonX.strOr(json['id']),
    code: JsonX.strOr(json['code']),
    name: JsonX.strOr(json['name']),
    isPaid: JsonX.boolOr(json['isPaid'], true),
    requiresAttachment: JsonX.boolOr(json['requiresAttachment']),
    allowsHalfDay: JsonX.boolOr(json['allowsHalfDay']),
    maxConsecutiveDays: JsonX.intOrNull(json['maxConsecutiveDays']),
  );

  final String id;
  final String code;
  final String name;
  final bool isPaid;
  final bool requiresAttachment;
  final bool allowsHalfDay;
  final int? maxConsecutiveDays;
}

/// `GET /leave-policies` item.
class LeavePolicy {
  const LeavePolicy({
    required this.id,
    required this.code,
    required this.name,
    this.description,
    this.accrualFrequency,
    this.annualEntitlementDays,
    this.carryForwardMaxDays,
    this.negativeBalanceAllowed = false,
    this.noticeDays,
    this.attachmentRequired = false,
  });

  factory LeavePolicy.fromJson(Map<String, dynamic> json) => LeavePolicy(
    id: JsonX.strOr(json['id']),
    code: JsonX.strOr(json['code']),
    name: JsonX.strOr(json['name']),
    description: JsonX.str(json['description']),
    accrualFrequency: JsonX.str(json['accrualFrequency']),
    annualEntitlementDays: JsonX.doubleOrNull(json['annualEntitlementDays']),
    carryForwardMaxDays: JsonX.doubleOrNull(json['carryForwardMaxDays']),
    negativeBalanceAllowed: JsonX.boolOr(json['negativeBalanceAllowed']),
    noticeDays: JsonX.intOrNull(json['noticeDays']),
    attachmentRequired: JsonX.boolOr(json['attachmentRequired']),
  );

  final String id;
  final String code;
  final String name;
  final String? description;
  final String? accrualFrequency;
  final double? annualEntitlementDays;
  final double? carryForwardMaxDays;
  final bool negativeBalanceAllowed;
  final int? noticeDays;
  final bool attachmentRequired;
}

/// `GET /leave-requests` item.
class LeaveRequest {
  const LeaveRequest({
    required this.id,
    required this.leaveType,
    this.leaveTypeId,
    required this.startDate,
    required this.endDate,
    this.units,
    this.dayType,
    required this.status,
    this.reason,
    this.submittedAt,
    this.decidedAt,
    this.approverName,
    this.rejectionReason,
    this.approvalTaskId,
  });

  factory LeaveRequest.fromJson(Map<String, dynamic> json) => LeaveRequest(
    id: JsonX.strOr(json['id']),
    leaveType: JsonX.strOr(json['leaveType']),
    leaveTypeId: JsonX.str(json['leaveTypeId']),
    startDate: JsonX.strOr(json['startDate']),
    endDate: JsonX.strOr(json['endDate']),
    units: JsonX.doubleOrNull(json['units']),
    dayType: JsonX.str(json['dayType']),
    status: JsonX.strOr(json['status'], 'Pending'),
    reason: JsonX.str(json['reason']),
    submittedAt: JsonX.str(json['submittedAt']),
    decidedAt: JsonX.str(json['decidedAt']),
    approverName: JsonX.str(json['approverName']),
    rejectionReason: JsonX.str(json['rejectionReason']),
    approvalTaskId: JsonX.str(json['approvalTaskId']),
  );

  final String id;
  final String leaveType;
  final String? leaveTypeId;
  final String startDate;
  final String endDate;
  final double? units;
  final String? dayType;
  final String status;
  final String? reason;
  final String? submittedAt;
  final String? decidedAt;
  final String? approverName;
  final String? rejectionReason;
  final String? approvalTaskId;

  bool get isCancellable => status == 'Pending' || status == 'Draft';
}

/// `GET /leave-requests/{id}` detail (adds decision history).
class LeaveRequestDetail extends LeaveRequest {
  const LeaveRequestDetail({
    required super.id,
    required super.leaveType,
    super.leaveTypeId,
    required super.startDate,
    required super.endDate,
    super.units,
    super.dayType,
    required super.status,
    super.reason,
    super.submittedAt,
    super.decidedAt,
    super.approverName,
    super.rejectionReason,
    super.approvalTaskId,
    this.decisionHistory = const [],
  });

  factory LeaveRequestDetail.fromJson(Map<String, dynamic> json) =>
      LeaveRequestDetail(
        id: JsonX.strOr(json['id']),
        leaveType: JsonX.strOr(json['leaveType']),
        leaveTypeId: JsonX.str(json['leaveTypeId']),
        startDate: JsonX.strOr(json['startDate']),
        endDate: JsonX.strOr(json['endDate']),
        units: JsonX.doubleOrNull(json['units']),
        dayType: JsonX.str(json['dayType']),
        status: JsonX.strOr(json['status'], 'Pending'),
        reason: JsonX.str(json['reason']),
        submittedAt: JsonX.str(json['submittedAt']),
        decidedAt: JsonX.str(json['decidedAt']),
        approverName: JsonX.str(json['approverName']),
        rejectionReason: JsonX.str(json['rejectionReason']),
        approvalTaskId: JsonX.str(json['approvalTaskId']),
        decisionHistory: JsonX.mapList(json['decisionHistory'])
            .map(DecisionEntry.fromJson)
            .toList(growable: false),
      );

  final List<DecisionEntry> decisionHistory;
}

/// `GET /leave/calendar` item (FR-EMP-045).
class LeaveCalendarEntry {
  const LeaveCalendarEntry({
    required this.date,
    required this.type,
    this.label,
    this.status,
  });

  factory LeaveCalendarEntry.fromJson(Map<String, dynamic> json) =>
      LeaveCalendarEntry(
        date: JsonX.strOr(json['date']),
        type: JsonX.strOr(json['type']),
        label: JsonX.str(json['label']),
        status: JsonX.str(json['status']),
      );

  final String date;
  final String type;
  final String? label;
  final String? status;
}
