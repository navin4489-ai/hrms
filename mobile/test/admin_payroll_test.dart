import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wintech_hrms/core/network/api_client.dart';
import 'package:wintech_hrms/core/storage/token_storage.dart';
import 'package:wintech_hrms/features/documents/my_pay_details_screen.dart';
import 'package:wintech_hrms/providers/admin_payroll_provider.dart';
import 'package:wintech_hrms/providers/api_provider.dart';

/// Fake transport: answers the payroll salary endpoints and records request shapes.
class _Transport implements HttpClientAdapter {
  final requests = <RequestOptions>[];

  static const _salaryJson = {
    'employeeId': 'emp-1',
    'employeeNumber': 'EMP001',
    'employeeName': 'Naveen Kumar',
    'department': 'Operations',
    'basicAnnual': 1200000,
    'hraAnnual': 480000,
    'daAnnual': 120000,
    'specialAllowanceAnnual': 200000,
    'otherAllowanceAnnual': 0,
    'employerPfAnnual': 0,
    'gratuityAnnual': 0,
    'professionalTaxMonthly': 200,
    'taxRegime': 'New',
    'deduction80CAnnual': 0,
    'deduction80DAnnual': 0,
    'homeLoanInterestAnnual': 0,
    'otherDeductionsAnnual': 0,
    'annualCtc': 2000000,
    'monthlyGross': 166666.67,
    'tax': {
      'regime': 'New',
      'financialYear': 2026,
      'annualGross': 2000000,
      'standardDeduction': 75000,
      'chapterViADeductions': 0,
      'taxableIncome': 1925000,
      'taxBeforeRebate': 185000,
      'rebate87A': 0,
      'cess': 7400,
      'annualTax': 192400,
      'monthlyTds': 16033.33,
    },
    'updatedAt': '2026-10-26T05:00:00Z',
  };

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    requests.add(options);
    if (options.path.endsWith('/me/salary')) {
      return _json(_salaryJson);
    }
    if (options.path.endsWith('/payroll/salaries')) {
      return _json([_salaryJson]);
    }
    if (options.path.endsWith('/payroll/salaries/preview-tax')) {
      return _json(_salaryJson['tax']);
    }
    if (options.path.contains('/payslips/generate')) {
      return _json({
        'periodId': 'period-1',
        'periodLabel': 'Oct 2026',
        'generated': 3,
        'skipped': 1,
        'skippedEmployees': ['EMP009'],
      });
    }
    if (options.method == 'PUT' && options.path.contains('/payroll/salaries/')) {
      return _json(_salaryJson);
    }
    return _json(const {});
  }

  ResponseBody _json(Object? body) => ResponseBody.fromString(
    jsonEncode(body),
    200,
    headers: {Headers.contentTypeHeader: [Headers.jsonContentType]},
  );

  @override
  void close({bool force = false}) {}
}

void main() {
  test('salary list parses pay details and the computed tax', () async {
    final transport = _Transport();
    final api = ApiClient(
      tokenStorage: InMemoryTokenStorage(),
      httpClientAdapter: transport,
    );
    final container = ProviderContainer(
      overrides: [apiClientProvider.overrideWithValue(api)],
    );
    addTearDown(container.dispose);
    addTearDown(api.close);

    final rows = await container.read(adminSalaryStructuresProvider.future);
    expect(rows, hasLength(1));
    final row = rows.single;
    expect(row.employeeNumber, 'EMP001');
    expect(row.annualCtc, 2000000);
    expect(row.monthlyGross, 166666.67);
    expect(row.taxRegime, 'New');
    expect(row.hasPayDetails, isTrue);
    // The computed tax comes from the server, not from any typed-in value.
    expect(row.tax.annualTax, 192400);
    expect(row.tax.monthlyTds, 16033.33);
    expect(row.tax.financialYear, 2026);
  });

  test('saving pay details PUTs the pay components and tax regime', () async {
    final transport = _Transport();
    final api = ApiClient(
      tokenStorage: InMemoryTokenStorage(),
      httpClientAdapter: transport,
    );
    final container = ProviderContainer(
      overrides: [apiClientProvider.overrideWithValue(api)],
    );
    addTearDown(container.dispose);
    addTearDown(api.close);

    final saved = await container
        .read(adminPayrollActionsProvider)
        .saveSalaryStructure('emp-1', {
          'basicAnnual': 1200000,
          'hraAnnual': 480000,
          'daAnnual': 120000,
          'specialAllowanceAnnual': 200000,
          'taxRegime': 'New',
          'professionalTaxMonthly': 200,
        });
    expect(saved.employeeNumber, 'EMP001');

    final request = transport.requests.singleWhere((r) => r.method == 'PUT');
    expect(request.path, contains('/payroll/salaries/emp-1'));
    final body = Map<String, dynamic>.from(request.data as Map);
    expect(body['basicAnnual'], 1200000);
    expect(body['taxRegime'], 'New');
    expect(body['professionalTaxMonthly'], 200);
  });

  test('tax preview posts unsaved values and returns the computed tax', () async {
    final transport = _Transport();
    final api = ApiClient(
      tokenStorage: InMemoryTokenStorage(),
      httpClientAdapter: transport,
    );
    final container = ProviderContainer(
      overrides: [apiClientProvider.overrideWithValue(api)],
    );
    addTearDown(container.dispose);
    addTearDown(api.close);

    final estimate = await container
        .read(adminPayrollActionsProvider)
        .previewTax({
          'basicAnnual': 1200000,
          'hraAnnual': 480000,
          'daAnnual': 120000,
          'specialAllowanceAnnual': 200000,
          'taxRegime': 'New',
        });
    expect(estimate.regime, 'New');
    expect(estimate.taxableIncome, 1925000);
    expect(estimate.annualTax, 192400);
    expect(estimate.monthlyTds, 16033.33);

    final request = transport.requests.singleWhere(
      (r) => r.path.endsWith('/preview-tax'),
    );
    expect(request.method, 'POST');
  });

  test('generate payslips posts to the period generate endpoint', () async {
    final transport = _Transport();
    final api = ApiClient(
      tokenStorage: InMemoryTokenStorage(),
      httpClientAdapter: transport,
    );
    final container = ProviderContainer(
      overrides: [apiClientProvider.overrideWithValue(api)],
    );
    addTearDown(container.dispose);
    addTearDown(api.close);

    final result = await container
        .read(adminPayrollActionsProvider)
        .generatePayslips('period-1');
    expect(result['generated'], 3);
    expect(result['skipped'], 1);
    expect(
      transport.requests.single.path,
      contains('/payroll-periods/period-1/payslips/generate'),
    );
  });

  test('my pay details reads own record from /me/salary', () async {
    final transport = _Transport();
    final api = ApiClient(
      tokenStorage: InMemoryTokenStorage(),
      httpClientAdapter: transport,
    );
    final container = ProviderContainer(
      overrides: [apiClientProvider.overrideWithValue(api)],
    );
    addTearDown(container.dispose);
    addTearDown(api.close);

    final row = await container.read(mySalaryProvider.future);
    expect(row.employeeNumber, 'EMP001');
    expect(row.annualCtc, 2000000);
    expect(row.taxRegime, 'New');
    expect(row.tax.monthlyTds, 16033.33);
    // Self-service only ever hits the caller's own endpoint — no id parameter.
    expect(transport.requests.single.path, endsWith('/me/salary'));
  });
}
