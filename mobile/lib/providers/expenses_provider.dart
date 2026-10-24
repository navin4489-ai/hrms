import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../core/network/api_client.dart';
import '../models/expense_models.dart';
import 'api_provider.dart';

/// Filter for `GET /expenses`.
class ExpenseQuery {
  const ExpenseQuery({this.status, this.month, this.team = false});

  final String? status;
  final String? month;
  final bool team;

  @override
  bool operator ==(Object other) =>
      other is ExpenseQuery &&
      other.status == status &&
      other.month == month &&
      other.team == team;

  @override
  int get hashCode => Object.hash(status, month, team);
}

/// `GET /expenses` paged result (FR-EMP-084).
class ExpensePage {
  const ExpensePage({
    required this.items,
    required this.page,
    required this.total,
    required this.hasNextPage,
  });

  factory ExpensePage.fromJson(Map<String, dynamic> json) {
    final rawItems = json['items'];
    return ExpensePage(
      items: (rawItems is List ? rawItems : const [])
          .whereType<Map>()
          .map((e) => Expense.fromJson(Map<String, dynamic>.from(e)))
          .toList(growable: false),
      page: (json['page'] as num?)?.toInt() ?? 1,
      total: (json['total'] as num?)?.toInt() ?? 0,
      hasNextPage: json['hasNextPage'] as bool? ?? false,
    );
  }

  final List<Expense> items;
  final int page;
  final int total;
  final bool hasNextPage;
}

final expensesProvider = FutureProvider.family<ExpensePage, ExpenseQuery>((
  ref,
  query,
) async {
  final api = ref.watch(apiClientProvider);
  final params = <String, dynamic>{'page': 1, 'pageSize': 50};
  if (query.status != null && query.status != 'All') {
    params['status'] = query.status;
  }
  if (query.month != null) params['month'] = query.month;
  if (query.team) params['team'] = 'true';
  final json = await api.getMap('/expenses', query: params);
  return ExpensePage.fromJson(json);
});

/// `GET /expenses/{id}` detail.
final expenseDetailProvider = FutureProvider.family<Expense, String>((
  ref,
  id,
) async {
  final api = ref.watch(apiClientProvider);
  final json = await api.getMap('/expenses/$id');
  return Expense.fromJson(json);
});

/// `GET /travel-requests` (FR-EMP-083).
final travelRequestsProvider = FutureProvider<List<TravelRequest>>((ref) async {
  final api = ref.watch(apiClientProvider);
  final json = await api.getMap(
    '/travel-requests',
    query: {'page': 1, 'pageSize': 50},
  );
  final items = json['items'] as List? ?? const [];
  return items
      .whereType<Map>()
      .map((e) => TravelRequest.fromJson(Map<String, dynamic>.from(e)))
      .toList(growable: false);
});

/// Expense + travel actions.
class ExpenseActions {
  ExpenseActions(this._api);

  final ApiClient _api;

  /// `POST /expenses` — multipart with optional receipt (FR-EMP-080).
  Future<Expense> submitExpense({
    required String category,
    required double amount,
    required String currency,
    required String expenseDate,
    required String businessPurpose,
    String? costCentre,
    String? receiptPath,
    String? receiptName,
  }) async {
    final fields = <String, dynamic>{
      'category': category,
      'amount': amount,
      'currency': currency,
      'expenseDate': expenseDate,
      'businessPurpose': businessPurpose,
      if (costCentre != null && costCentre.isNotEmpty) 'costCentre': costCentre,
    };
    if (receiptPath != null && receiptPath.isNotEmpty) {
      fields['receipt'] = await MultipartFile.fromFile(
        receiptPath,
        filename: receiptName,
      );
    }
    final json = await _api.postMultipart(
      '/expenses',
      formData: FormData.fromMap(fields),
    );
    return Expense.fromJson(json);
  }

  /// `POST /expenses/{id}/cancel`.
  Future<void> cancelExpense({
    required String id,
    required String reason,
  }) async {
    await _api.postMap('/expenses/$id/cancel', data: {'reason': reason});
  }

  /// `POST /travel-requests` -> 201 (FR-EMP-083).
  Future<TravelRequest> submitTravel({
    required String destination,
    required String purpose,
    required String startDate,
    required String endDate,
    required double estimatedCost,
    required String currency,
  }) async {
    final json = await _api.postMap(
      '/travel-requests',
      data: {
        'destination': destination,
        'purpose': purpose,
        'startDate': startDate,
        'endDate': endDate,
        'estimatedCost': estimatedCost,
        'currency': currency,
      },
    );
    return TravelRequest.fromJson(json);
  }
}

final expenseActionsProvider = Provider<ExpenseActions>(
  (ref) => ExpenseActions(ref.watch(apiClientProvider)),
);
