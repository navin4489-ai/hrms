import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/strings.dart';
import '../../core/utils/formatters.dart';
import '../../providers/expenses_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';
import '../../widgets/status_chip.dart';

/// Expense claims (FR-EMP-084) with status filter, new claim and travel
/// entry points.
class ExpensesScreen extends ConsumerStatefulWidget {
  const ExpensesScreen({super.key});

  @override
  ConsumerState<ExpensesScreen> createState() => _ExpensesScreenState();
}

class _ExpensesScreenState extends ConsumerState<ExpensesScreen> {
  String? _status = 'All';

  static const _filters = <String>[
    'All',
    'Pending',
    'Approved',
    'Rejected',
    'Paid',
    'Cancelled',
  ];

  @override
  Widget build(BuildContext context) {
    final query = ExpenseQuery(status: _status);
    final expenses = ref.watch(expensesProvider(query));

    return AppScaffold(
      title: Strings.expenses,
      actions: [
        IconButton(
          tooltip: Strings.travelRequests,
          icon: const Icon(Icons.flight_takeoff_outlined),
          onPressed: () => context.go('/more/expenses/travel'),
        ),
      ],
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => context.go('/more/expenses/new'),
        icon: const Icon(Icons.add_rounded),
        label: const Text(Strings.newExpense),
      ),
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(expensesProvider(query)),
        child: Column(
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 12, 16, 4),
              child: SingleChildScrollView(
                scrollDirection: Axis.horizontal,
                child: Row(
                  children: [
                    for (final status in _filters)
                      Padding(
                        padding: const EdgeInsets.only(right: 8),
                        child: ChoiceChip(
                          label: Text(status),
                          selected: _status == status,
                          onSelected: (_) => setState(() => _status = status),
                        ),
                      ),
                  ],
                ),
              ),
            ),
            Expanded(
              child: AsyncBody(
                value: expenses,
                onRetry: () => ref.invalidate(expensesProvider(query)),
                isEmpty: (page) => page.items.isEmpty,
                emptyTitle: Strings.noExpenses,
                emptyMessage: Strings.noExpensesHint,
                emptyIcon: Icons.receipt_outlined,
                builder: (page) => ListView.separated(
                  physics: const AlwaysScrollableScrollPhysics(),
                  padding: const EdgeInsets.fromLTRB(16, 8, 16, 88),
                  itemCount: page.items.length,
                  separatorBuilder: (_, __) => const SizedBox(height: 10),
                  itemBuilder: (context, index) {
                    final expense = page.items[index];
                    return Card(
                      clipBehavior: Clip.antiAlias,
                      child: ListTile(
                        leading: const Icon(Icons.receipt_long_rounded),
                        title: Text(expense.category),
                        subtitle: Text(
                          [
                            AppFormatters.currency(
                              expense.amount,
                              expense.currency,
                            ),
                            if (expense.expenseDate != null)
                              AppFormatters.date(expense.expenseDate),
                            if (expense.businessPurpose != null &&
                                expense.businessPurpose!.isNotEmpty)
                              expense.businessPurpose!,
                          ].join(' · '),
                          maxLines: 2,
                          overflow: TextOverflow.ellipsis,
                        ),
                        trailing: StatusChip(
                          status: expense.status,
                          dense: true,
                        ),
                        onTap: () => context.go('/more/expenses/${expense.id}'),
                      ),
                    );
                  },
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
