import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/strings.dart';
import '../../core/utils/formatters.dart';
import '../../providers/leave_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';
import '../../widgets/status_chip.dart';

/// Team members' leave requests for managers (requires `leave.approve` /
/// `attendance.view.team`; the server enforces it with 403).
class TeamLeaveScreen extends ConsumerStatefulWidget {
  const TeamLeaveScreen({super.key});

  @override
  ConsumerState<TeamLeaveScreen> createState() => _TeamLeaveScreenState();
}

class _TeamLeaveScreenState extends ConsumerState<TeamLeaveScreen> {
  String _status = '';

  static const _filters = <String, String>{
    '': Strings.all,
    'Pending': Strings.pending,
    'Approved': 'Approved',
    'Rejected': 'Rejected',
  };

  @override
  Widget build(BuildContext context) {
    final query = LeaveRequestQuery(
      status: _status.isEmpty ? null : _status,
      team: true,
    );
    final requests = ref.watch(leaveRequestsProvider(query));

    return AppScaffold(
      title: Strings.teamRequests,
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(leaveRequestsProvider(query)),
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
                value: requests,
                onRetry: () => ref.invalidate(leaveRequestsProvider(query)),
                isEmpty: (items) => items.isEmpty,
                emptyTitle: 'No team requests',
                emptyMessage:
                    'Leave requests from your team members will appear here.',
                emptyIcon: Icons.groups_outlined,
                builder: (items) => ListView.separated(
                  physics: const AlwaysScrollableScrollPhysics(),
                  padding: const EdgeInsets.fromLTRB(16, 8, 16, 16),
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
                          '${item.approverName != null ? ' · ${item.approverName}' : ''}',
                        ),
                        trailing: StatusChip(status: item.status, dense: true),
                        onTap: () => context.go('/leave/requests/${item.id}'),
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
