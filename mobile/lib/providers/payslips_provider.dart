import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../core/network/api_client.dart';
import '../models/payroll_models.dart';
import 'api_provider.dart';

/// `GET /payslips?year=YYYY` — own records only (FR-EMP-060, UAT-006).
/// Family key: year.
final payslipsProvider = FutureProvider.family<List<Payslip>, int>((
  ref,
  year,
) async {
  final api = ref.watch(apiClientProvider);
  final list = await api.getList('/payslips', query: {'year': year});
  return list
      .whereType<Map>()
      .map((e) => Payslip.fromJson(Map<String, dynamic>.from(e)))
      .toList(growable: false);
});

/// `GET /payslips/{id}` detail (earnings/deductions).
final payslipDetailProvider = FutureProvider.family<PayslipDetail, String>((
  ref,
  id,
) async {
  final api = ref.watch(apiClientProvider);
  final json = await api.getMap('/payslips/$id');
  return PayslipDetail.fromJson(json);
});

/// Payslip download helper: fetches PDF bytes from
/// `GET /payslips/{id}/download` (FR-EMP-062). The caller decides where to
/// save them (see documents_actions).
class PayrollActions {
  PayrollActions(this._api);

  final ApiClient _api;

  /// Returns raw PDF bytes for a payslip.
  Future<List<int>> downloadPayslip(String id) =>
      _api.downloadBytes('/payslips/$id/download');
}

final payrollActionsProvider = Provider<PayrollActions>(
  (ref) => PayrollActions(ref.watch(apiClientProvider)),
);
