import 'common.dart';

/// `GET /payslips` item (own records only).
class Payslip {
  const Payslip({
    required this.id,
    this.periodId,
    required this.periodLabel,
    this.startDate,
    this.endDate,
    this.gross,
    this.netPay,
    this.currency,
    this.status,
    this.publishedAt,
  });

  factory Payslip.fromJson(Map<String, dynamic> json) => Payslip(
    id: JsonX.strOr(json['id']),
    periodId: JsonX.str(json['periodId']),
    periodLabel: JsonX.strOr(json['periodLabel']),
    startDate: JsonX.str(json['startDate']),
    endDate: JsonX.str(json['endDate']),
    gross: JsonX.doubleOrNull(json['gross']),
    netPay: JsonX.doubleOrNull(json['netPay']),
    currency: JsonX.str(json['currency']),
    status: JsonX.str(json['status']),
    publishedAt: JsonX.str(json['publishedAt']),
  );

  final String id;
  final String? periodId;
  final String periodLabel;
  final String? startDate;
  final String? endDate;
  final double? gross;
  final double? netPay;
  final String? currency;
  final String? status;
  final String? publishedAt;
}

/// One `{ component, amount }` row on the payslip detail.
class PayComponent {
  const PayComponent({required this.component, required this.amount});

  factory PayComponent.fromJson(Map<String, dynamic> json) => PayComponent(
    component: JsonX.strOr(json['component']),
    amount: JsonX.doubleOrNull(json['amount']) ?? 0,
  );

  final String component;
  final double amount;
}

/// `GET /payslips/{id}` detail.
class PayslipDetail {
  const PayslipDetail({
    required this.id,
    required this.periodLabel,
    this.startDate,
    this.endDate,
    this.currency,
    this.earnings = const [],
    this.deductions = const [],
    this.gross,
    this.totalDeductions,
    this.netPay,
    this.ytdGross,
    this.employeeNumber,
    this.employeeName,
    this.legalEntity,
    this.generatedAt,
  });

  factory PayslipDetail.fromJson(Map<String, dynamic> json) => PayslipDetail(
    id: JsonX.strOr(json['id']),
    periodLabel: JsonX.strOr(json['periodLabel']),
    startDate: JsonX.str(json['startDate']),
    endDate: JsonX.str(json['endDate']),
    currency: JsonX.str(json['currency']),
    earnings: JsonX.mapList(json['earnings'])
        .map(PayComponent.fromJson)
        .toList(growable: false),
    deductions: JsonX.mapList(json['deductions'])
        .map(PayComponent.fromJson)
        .toList(growable: false),
    gross: JsonX.doubleOrNull(json['gross']),
    totalDeductions: JsonX.doubleOrNull(json['totalDeductions']),
    netPay: JsonX.doubleOrNull(json['netPay']),
    ytdGross: JsonX.doubleOrNull(json['ytdGross']),
    employeeNumber: JsonX.str(json['employeeNumber']),
    employeeName: JsonX.str(json['employeeName']),
    legalEntity: JsonX.str(json['legalEntity']),
    generatedAt: JsonX.str(json['generatedAt']),
  );

  final String id;
  final String periodLabel;
  final String? startDate;
  final String? endDate;
  final String? currency;
  final List<PayComponent> earnings;
  final List<PayComponent> deductions;
  final double? gross;
  final double? totalDeductions;
  final double? netPay;
  final double? ytdGross;
  final String? employeeNumber;
  final String? employeeName;
  final String? legalEntity;
  final String? generatedAt;
}
