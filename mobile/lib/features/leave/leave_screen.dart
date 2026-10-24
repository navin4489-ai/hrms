import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/strings.dart';
import '../../core/utils/formatters.dart';
import '../../models/me_models.dart';
import '../../providers/auth_provider.dart';
import '../../providers/leave_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';
import '../../widgets/section_card.dart';
import '../../widgets/status_chip.dart';

/// Leave hub (FR-EMP-040..047): balances + policies tab, my requests tab,
/// plus calendar / team / new-request entry points.
class LeaveScreen extends ConsumerStatefulWidget {
  const LeaveScreen({super.key});

  @override
  ConsumerState<LeaveScreen> createState() => _LeaveScreenState();
}

class _LeaveScreenState extends ConsumerState<LeaveScreen> {
  String _status = '';

  static const _filters = <String, String>{
    '': Strings.all,
    'Pending': Strings.pending,
    'Approved': 'Approved',
    'Rejected': 'Rejected',
    'Cancelled': 'Cancelled',
  };

  @override
  Widget build(BuildContext context) {
    final year = DateTime.now().year;
    final auth = ref.watch(authStateProvider);
    final canSeeTeam = auth.hasAnyPermission([
      'leave.approve',
      'attendance.view.team',
    ]);
    final query = LeaveRequestQuery(status: _status.isEmpty ? null : _status);

    return DefaultTabController(
      length: 2,
      child: AppScaffold(
        title: Strings.leaveTitle,
        actions: [
          IconButton(
            tooltip: Strings.leaveCalendar,
            icon: const Icon(Icons.calendar_month_outlined),
            onPressed: () => context.go('/leave/calendar'),
          ),
          if (canSeeTeam)
            IconButton(
              tooltip: Strings.teamRequests,
              icon: const Icon(Icons.groups_outlined),
              onPressed: () => context.go('/leave/team'),
            ),
        ],
        floatingActionButton: FloatingActionButton.extended(
          onPressed: () => context.go('/leave/new'),
          icon: const Icon(Icons.add_rounded),
          label: const Text('Request'),
        ),
        bottom: const TabBar(
          tabs: [
            Tab(text: Strings.balances),
            Tab(text: Strings.myLeaveRequests),
          ],
        ),
        body: TabBarView(
          children: [
            // ------------------------------------------------------ balances
            RefreshIndicator(
              onRefresh: () async {
                ref.invalidate(leaveBalancesProvider);
                ref.invalidate(leavePoliciesProvider);
              },
              child: AsyncBody(
                value: ref.watch(leaveBalancesProvider(year)),
                onRetry: () => ref.invalidate(leaveBalancesProvider),
                isEmpty: (items) => items.isEmpty,
                emptyTitle: 'No balances',
                emptyMessage: 'Leave balances for $year will appear here.',
                builder: (balances) => ListView(
                  physics: const AlwaysScrollableScrollPhysics(),
                  padding: const EdgeInsets.fromLTRB(16, 16, 16, 96),
                  children: [
                    SectionCard(
                      title: '$year ${Strings.balances}',
                      child: Column(
                        children: [
                          for (final balance in balances)
                            _BalanceTile(balance: balance),
                        ],
                      ),
                    ),
                    const SizedBox(height: 12),
                    const _PoliciesCard(),
                  ],
                ),
              ),
            ),
            // ----------------------------------------------------- requests
            RefreshIndicator(
              onRefresh: () async =>
                  ref.invalidate(leaveRequestsProvider(query)),
              child: Column(
                children: [
                  Padding(
                    padding: const EdgeInsets.fromLTRB(16, 12, 16, 4),
                    child: SingleChildScrollView(
                      scrollDirection: Axis.horizontal,
                      child: Row(
                        children: [
                          for (final entry in _filters.entries)
                            Padding(
                              padding: const EdgeInsets.only(right: 8),
                              child: ChoiceChip(
                                label: Text(entry.value),
                                selected: _status == entry.key,
                                onSelected: (_) =>
                                    setState(() => _status = entry.key),
                              ),
                            ),
                        ],
                      ),
                    ),
                  ),
                  Expanded(
                    child: AsyncBody(
                      value: ref.watch(leaveRequestsProvider(query)),
                      onRetry: () =>
                          ref.invalidate(leaveRequestsProvider(query)),
                      isEmpty: (items) => items.isEmpty,
                      emptyTitle: Strings.noLeaveRequests,
                      emptyMessage: Strings.noLeaveRequestsHint,
                      emptyIcon: Icons.beach_access_rounded,
                      builder: (items) => ListView.separated(
                        physics: const AlwaysScrollableScrollPhysics(),
                        padding: const EdgeInsets.fromLTRB(16, 8, 16, 96),
                        itemCount: items.length,
                        separatorBuilder: (_, __) => const SizedBox(height: 10),
                        itemBuilder: (context, index) {
                          final item = items[index];
                          return Card(
                            clipBehavior: Clip.antiAlias,
                            child: ListTile(
                              title: Text(item.leaveType),
                              subtitle: Text(
                                '${AppFormatters.dateShort(item.startDate)} – '
                                '${AppFormatters.date(item.endDate)}'
                                '${item.units != null ? ' · ${AppFormatters.days(item.units)} d' : ''}',
                              ),
                              trailing: StatusChip(
                                status: item.status,
                                dense: true,
                              ),
                              onTap: () =>
                                  context.go('/leave/requests/${item.id}'),
                            ),
                          );
                        },
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _BalanceTile extends StatelessWidget {
  const _BalanceTile({required this.balance});

  final LeaveBalance balance;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 6),
      child: Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  balance.name,
                  style: theme.textTheme.bodyMedium?.copyWith(
                    fontWeight: FontWeight.w600,
                  ),
                ),
                Text(
                  'Used ${AppFormatters.days(balance.used)} · '
                  'Pending ${AppFormatters.days(balance.pending)} · '
                  'Accrued ${AppFormatters.days(balance.accrued)}',
                  style: theme.textTheme.bodySmall?.copyWith(
                    color: scheme.onSurfaceVariant,
                  ),
                ),
              ],
            ),
          ),
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
            decoration: BoxDecoration(
              color: scheme.primaryContainer,
              borderRadius: BorderRadius.circular(10),
            ),
            child: Text(
              '${AppFormatters.days(balance.available)} ${balance.unit}',
              style: theme.textTheme.titleMedium?.copyWith(
                fontWeight: FontWeight.w800,
                color: scheme.onPrimaryContainer,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _PoliciesCard extends ConsumerWidget {
  const _PoliciesCard();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final policies = ref.watch(leavePoliciesProvider);
    return SectionCard(
      title: 'Leave policies',
      child: AsyncBody(
        value: policies,
        onRetry: () => ref.invalidate(leavePoliciesProvider),
        isEmpty: (items) => items.isEmpty,
        emptyTitle: 'No policies',
        emptyMessage: 'Leave policy details are not available yet.',
        padding: EdgeInsets.zero,
        builder: (items) => Column(
          children: [
            for (final policy in items)
              ExpansionTile(
                tilePadding: EdgeInsets.zero,
                childrenPadding: const EdgeInsets.only(
                  left: 4,
                  bottom: 12,
                  right: 4,
                ),
                title: Text(
                  policy.name,
                  style: theme.textTheme.bodyMedium?.copyWith(
                    fontWeight: FontWeight.w600,
                  ),
                ),
                subtitle: Text(
                  [
                    if (policy.annualEntitlementDays != null)
                      '${AppFormatters.days(policy.annualEntitlementDays)} days/year',
                    if (policy.noticeDays != null)
                      '${policy.noticeDays} days notice',
                    if (policy.attachmentRequired) 'Attachment required',
                  ].join(' · '),
                  style: theme.textTheme.bodySmall,
                ),
                children: [
                  if (policy.description != null &&
                      policy.description!.isNotEmpty)
                    Align(
                      alignment: Alignment.centerLeft,
                      child: Text(policy.description!),
                    ),
                ],
              ),
          ],
        ),
      ),
    );
  }
}
