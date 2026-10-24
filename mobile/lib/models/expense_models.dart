import 'common.dart';

/// `GET /expenses` item.
class Expense {
  const Expense({
    required this.id,
    required this.category,
    required this.amount,
    required this.currency,
    this.expenseDate,
    this.businessPurpose,
    this.costCentre,
    required this.status,
    this.submittedAt,
    this.approvedAt,
    this.paidReference,
    this.rejectionReason,
    this.approvalTaskId,
    this.receiptUrl,
  });

  factory Expense.fromJson(Map<String, dynamic> json) => Expense(
    id: JsonX.strOr(json['id']),
    category: JsonX.strOr(json['category']),
    amount: JsonX.doubleOrNull(json['amount']) ?? 0,
    currency: JsonX.strOr(json['currency'], 'INR'),
    expenseDate: JsonX.str(json['expenseDate']),
    businessPurpose: JsonX.str(json['businessPurpose']),
    costCentre: JsonX.str(json['costCentre']),
    status: JsonX.strOr(json['status'], 'Pending'),
    submittedAt: JsonX.str(json['submittedAt']),
    approvedAt: JsonX.str(json['approvedAt']),
    paidReference: JsonX.str(json['paidReference']),
    rejectionReason: JsonX.str(json['rejectionReason']),
    approvalTaskId: JsonX.str(json['approvalTaskId']),
    receiptUrl: JsonX.str(json['receiptUrl']),
  );

  final String id;
  final String category;
  final double amount;
  final String currency;
  final String? expenseDate;
  final String? businessPurpose;
  final String? costCentre;
  final String status;
  final String? submittedAt;
  final String? approvedAt;
  final String? paidReference;
  final String? rejectionReason;
  final String? approvalTaskId;
  final String? receiptUrl;

  bool get canCancel => status == 'Pending' || status == 'Draft';
}

/// `GET /travel-requests` item.
class TravelRequest {
  const TravelRequest({
    required this.id,
    required this.destination,
    this.purpose,
    this.startDate,
    this.endDate,
    this.estimatedCost,
    this.currency,
    required this.status,
    this.submittedAt,
    this.rejectionReason,
    this.approvalTaskId,
  });

  factory TravelRequest.fromJson(Map<String, dynamic> json) => TravelRequest(
    id: JsonX.strOr(json['id']),
    destination: JsonX.strOr(json['destination']),
    purpose: JsonX.str(json['purpose']),
    startDate: JsonX.str(json['startDate']),
    endDate: JsonX.str(json['endDate']),
    estimatedCost: JsonX.doubleOrNull(json['estimatedCost']),
    currency: JsonX.str(json['currency']),
    status: JsonX.strOr(json['status'], 'Pending'),
    submittedAt: JsonX.str(json['submittedAt']),
    rejectionReason: JsonX.str(json['rejectionReason']),
    approvalTaskId: JsonX.str(json['approvalTaskId']),
  );

  final String id;
  final String destination;
  final String? purpose;
  final String? startDate;
  final String? endDate;
  final double? estimatedCost;
  final String? currency;
  final String status;
  final String? submittedAt;
  final String? rejectionReason;
  final String? approvalTaskId;
}
