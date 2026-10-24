import 'attendance_models.dart' show DecisionEntry;
import 'common.dart';

/// `GET /approvals/pending` item.
class ApprovalTask {
  const ApprovalTask({
    required this.taskId,
    required this.requestType,
    required this.requestId,
    required this.title,
    this.requesterName,
    this.requesterEmployeeNumber,
    this.requestedAt,
    this.stepName,
    this.dueAt,
    this.amount,
    this.currency,
    this.status = 'Pending',
    this.deepLink,
  });

  factory ApprovalTask.fromJson(Map<String, dynamic> json) => ApprovalTask(
    taskId: JsonX.strOr(json['taskId']),
    requestType: JsonX.strOr(json['requestType']),
    requestId: JsonX.strOr(json['requestId']),
    title: JsonX.strOr(json['title']),
    requesterName: JsonX.str(json['requesterName']),
    requesterEmployeeNumber: JsonX.str(json['requesterEmployeeNumber']),
    requestedAt: JsonX.str(json['requestedAt']),
    stepName: JsonX.str(json['stepName']),
    dueAt: JsonX.str(json['dueAt']),
    amount: JsonX.doubleOrNull(json['amount']),
    currency: JsonX.str(json['currency']),
    status: JsonX.strOr(json['status'], 'Pending'),
    deepLink: JsonX.str(json['deepLink']),
  );

  final String taskId;
  final String requestType;
  final String requestId;
  final String title;
  final String? requesterName;
  final String? requesterEmployeeNumber;
  final String? requestedAt;
  final String? stepName;
  final String? dueAt;
  final double? amount;
  final String? currency;
  final String status;
  final String? deepLink;

  bool get isOverdue {
    final due = dueAt == null ? null : DateTime.tryParse(dueAt!);
    return due != null && due.toUtc().isBefore(DateTime.now().toUtc());
  }
}

/// `GET /approvals/stats` response.
class ApprovalStats {
  const ApprovalStats({
    this.pending = 0,
    this.overdue = 0,
    this.completedThisMonth = 0,
  });

  factory ApprovalStats.fromJson(Map<String, dynamic> json) => ApprovalStats(
    pending: JsonX.intOrNull(json['pending']) ?? 0,
    overdue: JsonX.intOrNull(json['overdue']) ?? 0,
    completedThisMonth: JsonX.intOrNull(json['completedThisMonth']) ?? 0,
  );

  final int pending;
  final int overdue;
  final int completedThisMonth;
}

/// `GET /approvals/mine` item.
class MyWorkflowTask {
  const MyWorkflowTask({
    required this.taskId,
    required this.requestType,
    required this.requestId,
    required this.title,
    required this.status,
    this.currentStep,
    this.approverName,
    this.updatedAt,
  });

  factory MyWorkflowTask.fromJson(Map<String, dynamic> json) => MyWorkflowTask(
    taskId: JsonX.strOr(json['taskId']),
    requestType: JsonX.strOr(json['requestType']),
    requestId: JsonX.strOr(json['requestId']),
    title: JsonX.strOr(json['title']),
    status: JsonX.strOr(json['status'], 'Pending'),
    currentStep: JsonX.str(json['currentStep']),
    approverName: JsonX.str(json['approverName']),
    updatedAt: JsonX.str(json['updatedAt']),
  );

  final String taskId;
  final String requestType;
  final String requestId;
  final String title;
  final String status;
  final String? currentStep;
  final String? approverName;
  final String? updatedAt;
}

/// `GET /approvals/{taskId}` detail.
class ApprovalTaskDetail {
  const ApprovalTaskDetail({
    required this.taskId,
    required this.requestType,
    required this.requestId,
    required this.title,
    required this.status,
    this.currentStep,
    this.dueAt,
    this.requesterName,
    this.canDecide = false,
    this.history = const [],
  });

  factory ApprovalTaskDetail.fromJson(Map<String, dynamic> json) =>
      ApprovalTaskDetail(
        taskId: JsonX.strOr(json['taskId']),
        requestType: JsonX.strOr(json['requestType']),
        requestId: JsonX.strOr(json['requestId']),
        title: JsonX.strOr(json['title']),
        status: JsonX.strOr(json['status'], 'Pending'),
        currentStep: JsonX.str(json['currentStep']),
        dueAt: JsonX.str(json['dueAt']),
        requesterName: JsonX.str(json['requesterName']),
        canDecide: JsonX.boolOr(json['canDecide']),
        history: JsonX.mapList(json['history'])
            .map(DecisionEntry.fromJson)
            .toList(growable: false),
      );

  final String taskId;
  final String requestType;
  final String requestId;
  final String title;
  final String status;
  final String? currentStep;
  final String? dueAt;
  final String? requesterName;
  final bool canDecide;
  final List<DecisionEntry> history;
}

/// `POST /approvals/{taskId}/decision` response.
class DecisionResult {
  const DecisionResult({
    required this.taskId,
    required this.status,
    this.completed = false,
    this.message,
  });

  factory DecisionResult.fromJson(Map<String, dynamic> json) => DecisionResult(
    taskId: JsonX.strOr(json['taskId']),
    status: JsonX.strOr(json['status']),
    completed: JsonX.boolOr(json['completed']),
    message: JsonX.str(json['message']),
  );

  final String taskId;
  final String status;
  final bool completed;
  final String? message;
}
