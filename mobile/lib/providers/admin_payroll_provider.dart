import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../models/common.dart';
import 'api_provider.dart';

/// Computed annual tax for one employee's pay details.
class PayrollTaxEstimate {
  const PayrollTaxEstimate({
    required this.regime,
    required this.financialYear,
    required this.annualGross,
    required this.standardDeduction,
    required this.taxableIncome,
    required this.taxBeforeRebate,
    required this.rebate,
    required this.cess,
    required this.annualTax,
    required this.monthlyTds,
  });

  factory PayrollTaxEstimate.fromJson(Map<String, dynamic> json) =>
      PayrollTaxEstimate(
        regime: JsonX.strOr(json['regime'], 'New'),
        financialYear: JsonX.intOrNull(json['financialYear']) ?? 0,
        annualGross: JsonX.numOrNull(json['annualGross']) ?? 0,
        standardDeduction: JsonX.numOrNull(json['standardDeduction']) ?? 0,
        taxableIncome: JsonX.numOrNull(json['taxableIncome']) ?? 0,
        taxBeforeRebate: JsonX.numOrNull(json['taxBeforeRebate']) ?? 0,
        rebate: JsonX.numOrNull(json['rebate87A']) ?? 0,
        cess: JsonX.numOrNull(json['cess']) ?? 0,
        annualTax: JsonX.numOrNull(json['annualTax']) ?? 0,
        monthlyTds: JsonX.numOrNull(json['monthlyTds']) ?? 0,
      );

  final String regime;
  final int financialYear;
  final num annualGross;
  final num standardDeduction;
  final num taxableIncome;
  final num taxBeforeRebate;
  final num rebate;
  final num cess;
  final num annualTax;
  final num monthlyTds;
}

/// One employee's finance-maintained pay details plus the computed tax.
class AdminSalaryStructure {
  const AdminSalaryStructure({
    required this.employeeId,
    required this.employeeNumber,
    required this.employeeName,
    this.department,
    required this.basicAnnual,
    required this.hraAnnual,
    required this.daAnnual,
    required this.specialAllowanceAnnual,
    required this.otherAllowanceAnnual,
    required this.employerPfAnnual,
    required this.gratuityAnnual,
    required this.professionalTaxMonthly,
    required this.taxRegime,
    required this.deduction80CAnnual,
    required this.deduction80DAnnual,
    required this.homeLoanInterestAnnual,
    required this.otherDeductionsAnnual,
    required this.annualCtc,
    required this.monthlyGross,
    required this.tax,
    this.updatedAt,
  });

  factory AdminSalaryStructure.fromJson(Map<String, dynamic> json) =>
      AdminSalaryStructure(
        employeeId: JsonX.strOr(json['employeeId']),
        employeeNumber: JsonX.strOr(json['employeeNumber']),
        employeeName: JsonX.strOr(json['employeeName']),
        department: JsonX.str(json['department']),
        basicAnnual: JsonX.numOrNull(json['basicAnnual']) ?? 0,
        hraAnnual: JsonX.numOrNull(json['hraAnnual']) ?? 0,
        daAnnual: JsonX.numOrNull(json['daAnnual']) ?? 0,
        specialAllowanceAnnual:
            JsonX.numOrNull(json['specialAllowanceAnnual']) ?? 0,
        otherAllowanceAnnual:
            JsonX.numOrNull(json['otherAllowanceAnnual']) ?? 0,
        employerPfAnnual: JsonX.numOrNull(json['employerPfAnnual']) ?? 0,
        gratuityAnnual: JsonX.numOrNull(json['gratuityAnnual']) ?? 0,
        professionalTaxMonthly:
            JsonX.numOrNull(json['professionalTaxMonthly']) ?? 0,
        taxRegime: JsonX.strOr(json['taxRegime'], 'New'),
        deduction80CAnnual: JsonX.numOrNull(json['deduction80CAnnual']) ?? 0,
        deduction80DAnnual: JsonX.numOrNull(json['deduction80DAnnual']) ?? 0,
        homeLoanInterestAnnual:
            JsonX.numOrNull(json['homeLoanInterestAnnual']) ?? 0,
        otherDeductionsAnnual:
            JsonX.numOrNull(json['otherDeductionsAnnual']) ?? 0,
        annualCtc: JsonX.numOrNull(json['annualCtc']) ?? 0,
        monthlyGross: JsonX.numOrNull(json['monthlyGross']) ?? 0,
        tax: PayrollTaxEstimate.fromJson(
          JsonX.mapOrNull(json['tax']),
        ),
        updatedAt: JsonX.dateTimeOrNull(json['updatedAt']),
      );

  final String employeeId;
  final String employeeNumber;
  final String employeeName;
  final String? department;
  final num basicAnnual;
  final num hraAnnual;
  final num daAnnual;
  final num specialAllowanceAnnual;
  final num otherAllowanceAnnual;
  final num employerPfAnnual;
  final num gratuityAnnual;
  final num professionalTaxMonthly;
  final String taxRegime;
  final num deduction80CAnnual;
  final num deduction80DAnnual;
  final num homeLoanInterestAnnual;
  final num otherDeductionsAnnual;
  final num annualCtc;
  final num monthlyGross;
  final PayrollTaxEstimate tax;
  final DateTime? updatedAt;

  bool get hasPayDetails => annualCtc > 0;

  /// Body accepted by PUT /payroll/salaries/{employeeId} and .../preview-tax.
  Map<String, dynamic> toRequestJson() => {
    'basicAnnual': basicAnnual,
    'hraAnnual': hraAnnual,
    'daAnnual': daAnnual,
    'specialAllowanceAnnual': specialAllowanceAnnual,
    'otherAllowanceAnnual': otherAllowanceAnnual,
    'employerPfAnnual': employerPfAnnual,
    'gratuityAnnual': gratuityAnnual,
    'professionalTaxMonthly': professionalTaxMonthly,
    'taxRegime': taxRegime,
    'deduction80CAnnual': deduction80CAnnual,
    'deduction80DAnnual': deduction80DAnnual,
    'homeLoanInterestAnnual': homeLoanInterestAnnual,
    'otherDeductionsAnnual': otherDeductionsAnnual,
  };
}

/// `GET /payroll/salaries` — every active employee with pay details + computed tax.
final adminSalaryStructuresProvider =
    FutureProvider<List<AdminSalaryStructure>>((ref) async {
      final list = await ref.watch(apiClientProvider).getList(
        '/payroll/salaries',
      );
      return list
          .whereType<Map>()
          .map(
            (item) => AdminSalaryStructure.fromJson(
              Map<String, dynamic>.from(item),
            ),
          )
          .toList(growable: false);
    });

/// Actions for finance: save pay details, preview tax from unsaved values, generate payslips.
class AdminPayrollActions {
  const AdminPayrollActions(this._ref);

  final Ref _ref;

  Future<AdminSalaryStructure> saveSalaryStructure(
    String employeeId,
    Map<String, dynamic> body,
  ) async {
    final json = await _ref
        .read(apiClientProvider)
        .putMap('/payroll/salaries/$employeeId', data: body);
    _ref.invalidate(adminSalaryStructuresProvider);
    return AdminSalaryStructure.fromJson(json);
  }

  Future<PayrollTaxEstimate> previewTax(Map<String, dynamic> body) async {
    final json = await _ref
        .read(apiClientProvider)
        .postMap('/payroll/salaries/preview-tax', data: body);
    return PayrollTaxEstimate.fromJson(json);
  }

  /// Auto-calculate payslips for a period from every employee's pay details.
  Future<Map<String, dynamic>> generatePayslips(String periodId) =>
      _ref.read(apiClientProvider).postMap(
        '/payroll-periods/$periodId/payslips/generate',
      );
}

final adminPayrollActionsProvider = Provider<AdminPayrollActions>(
  (ref) => AdminPayrollActions(ref),
);
