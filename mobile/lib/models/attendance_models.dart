import 'common.dart';

/// One attendance day (`GET /attendance` item / `GET /attendance/today`).
class AttendanceRecord {
  const AttendanceRecord({
    required this.workDate,
    required this.status,
    this.shiftName,
    this.checkIn,
    this.checkOut,
    this.workedHours,
    this.lateByMinutes,
    this.isHalfDay = false,
    this.source,
    this.correctionStatus,
  });

  factory AttendanceRecord.fromJson(Map<String, dynamic> json) =>
      AttendanceRecord(
        workDate: JsonX.strOr(json['workDate']),
        status: JsonX.strOr(json['status'], 'NotMarked'),
        shiftName: JsonX.str(json['shiftName']),
        checkIn: JsonX.str(json['checkIn']),
        checkOut: JsonX.str(json['checkOut']),
        workedHours: JsonX.doubleOrNull(json['workedHours']),
        lateByMinutes: JsonX.intOrNull(json['lateByMinutes']),
        isHalfDay: JsonX.boolOr(json['isHalfDay']),
        source: JsonX.str(json['source']),
        correctionStatus: JsonX.str(json['correctionStatus']),
      );

  final String workDate;
  final String status;
  final String? shiftName;
  final String? checkIn;
  final String? checkOut;
  final double? workedHours;
  final int? lateByMinutes;
  final bool isHalfDay;
  final String? source;
  final String? correctionStatus;

  bool get hasCorrection => correctionStatus != null;
}

/// `summary` object on `GET /attendance`.
class AttendanceMonthSummary {
  const AttendanceMonthSummary({
    this.present = 0,
    this.absent = 0,
    this.halfDay = 0,
    this.leave = 0,
    this.holidays = 0,
    this.weekOffs = 0,
    this.late = 0,
    this.pendingCorrections = 0,
  });

  factory AttendanceMonthSummary.fromJson(Map<String, dynamic> json) =>
      AttendanceMonthSummary(
        present: JsonX.intOrNull(json['present']) ?? 0,
        absent: JsonX.intOrNull(json['absent']) ?? 0,
        halfDay: JsonX.intOrNull(json['halfDay']) ?? 0,
        leave: JsonX.intOrNull(json['leave']) ?? 0,
        holidays: JsonX.intOrNull(json['holidays']) ?? 0,
        weekOffs: JsonX.intOrNull(json['weekOffs']) ?? 0,
        late: JsonX.intOrNull(json['late']) ?? 0,
        pendingCorrections: JsonX.intOrNull(json['pendingCorrections']) ?? 0,
      );

  final int present;
  final int absent;
  final int halfDay;
  final int leave;
  final int holidays;
  final int weekOffs;
  final int late;
  final int pendingCorrections;
}

/// `POST /attendance/punch` response.
class PunchResult {
  const PunchResult({
    this.workDate,
    this.checkIn,
    this.checkOut,
    this.status,
    this.message,
  });

  factory PunchResult.fromJson(Map<String, dynamic> json) => PunchResult(
    workDate: JsonX.str(json['workDate']),
    checkIn: JsonX.str(json['checkIn']),
    checkOut: JsonX.str(json['checkOut']),
    status: JsonX.str(json['status']),
    message: JsonX.str(json['message']),
  );

  final String? workDate;
  final String? checkIn;
  final String? checkOut;
  final String? status;
  final String? message;
}

/// `GET /attendance/summary` response.
class AttendanceRangeSummary {
  const AttendanceRangeSummary({
    this.present = 0,
    this.absent = 0,
    this.halfDay = 0,
    this.leave = 0,
    this.holidays = 0,
    this.weekOffs = 0,
    this.late = 0,
    this.overtimeHours = 0,
    this.workedHours = 0,
  });

  factory AttendanceRangeSummary.fromJson(Map<String, dynamic> json) =>
      AttendanceRangeSummary(
        present: JsonX.intOrNull(json['present']) ?? 0,
        absent: JsonX.intOrNull(json['absent']) ?? 0,
        halfDay: JsonX.intOrNull(json['halfDay']) ?? 0,
        leave: JsonX.intOrNull(json['leave']) ?? 0,
        holidays: JsonX.intOrNull(json['holidays']) ?? 0,
        weekOffs: JsonX.intOrNull(json['weekOffs']) ?? 0,
        late: JsonX.intOrNull(json['late']) ?? 0,
        overtimeHours: JsonX.doubleOrNull(json['overtimeHours']) ?? 0,
        workedHours: JsonX.doubleOrNull(json['workedHours']) ?? 0,
      );

  final int present;
  final int absent;
  final int halfDay;
  final int leave;
  final int holidays;
  final int weekOffs;
  final int late;
  final double overtimeHours;
  final double workedHours;
}

/// A decision entry shared by corrections/leave/approval histories.
class DecisionEntry {
  const DecisionEntry({
    this.action,
    this.actorName,
    this.comment,
    this.at,
    this.step,
  });

  factory DecisionEntry.fromJson(Map<String, dynamic> json) => DecisionEntry(
    action: JsonX.str(json['action']),
    actorName: JsonX.str(json['actorName']),
    comment: JsonX.str(json['comment']),
    at: JsonX.str(json['at']),
    step: JsonX.str(json['step']),
  );

  final String? action;
  final String? actorName;
  final String? comment;
  final String? at;
  final String? step;
}

/// `GET /attendance-corrections` item.
class AttendanceCorrection {
  const AttendanceCorrection({
    required this.id,
    required this.workDate,
    required this.type,
    this.proposedCheckIn,
    this.proposedCheckOut,
    this.originalCheckIn,
    this.originalCheckOut,
    this.reason,
    required this.status,
    this.submittedAt,
    this.approverName,
    this.approvalTaskId,
  });

  factory AttendanceCorrection.fromJson(Map<String, dynamic> json) =>
      AttendanceCorrection(
        id: JsonX.strOr(json['id']),
        workDate: JsonX.strOr(json['workDate']),
        type: JsonX.strOr(json['type']),
        proposedCheckIn: JsonX.str(json['proposedCheckIn']),
        proposedCheckOut: JsonX.str(json['proposedCheckOut']),
        originalCheckIn: JsonX.str(json['originalCheckIn']),
        originalCheckOut: JsonX.str(json['originalCheckOut']),
        reason: JsonX.str(json['reason']),
        status: JsonX.strOr(json['status'], 'Pending'),
        submittedAt: JsonX.str(json['submittedAt']),
        approverName: JsonX.str(json['approverName']),
        approvalTaskId: JsonX.str(json['approvalTaskId']),
      );

  final String id;
  final String workDate;
  final String type;
  final String? proposedCheckIn;
  final String? proposedCheckOut;
  final String? originalCheckIn;
  final String? originalCheckOut;
  final String? reason;
  final String status;
  final String? submittedAt;
  final String? approverName;
  final String? approvalTaskId;
}

/// `GET /attendance-corrections/{id}` detail adds decision history.
class AttendanceCorrectionDetail extends AttendanceCorrection {
  const AttendanceCorrectionDetail({
    required super.id,
    required super.workDate,
    required super.type,
    super.proposedCheckIn,
    super.proposedCheckOut,
    super.originalCheckIn,
    super.originalCheckOut,
    super.reason,
    required super.status,
    super.submittedAt,
    super.approverName,
    super.approvalTaskId,
    this.decisionHistory = const [],
  });

  factory AttendanceCorrectionDetail.fromJson(Map<String, dynamic> json) =>
      AttendanceCorrectionDetail(
        id: JsonX.strOr(json['id']),
        workDate: JsonX.strOr(json['workDate']),
        type: JsonX.strOr(json['type']),
        proposedCheckIn: JsonX.str(json['proposedCheckIn']),
        proposedCheckOut: JsonX.str(json['proposedCheckOut']),
        originalCheckIn: JsonX.str(json['originalCheckIn']),
        originalCheckOut: JsonX.str(json['originalCheckOut']),
        reason: JsonX.str(json['reason']),
        status: JsonX.strOr(json['status'], 'Pending'),
        submittedAt: JsonX.str(json['submittedAt']),
        approverName: JsonX.str(json['approverName']),
        approvalTaskId: JsonX.str(json['approvalTaskId']),
        decisionHistory: JsonX.mapList(json['decisionHistory'])
            .map(DecisionEntry.fromJson)
            .toList(growable: false),
      );

  final List<DecisionEntry> decisionHistory;
}
