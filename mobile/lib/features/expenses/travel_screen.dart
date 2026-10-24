import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/strings.dart';
import '../../core/utils/formatters.dart';
import '../../providers/expenses_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';
import '../../widgets/status_chip.dart';

/// Travel requests (FR-EMP-083) with new-request entry point.
class TravelScreen extends ConsumerWidget {
  const TravelScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final travel = ref.watch(travelRequestsProvider);

    return AppScaffold(
      title: Strings.travelRequests,
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => context.go('/more/expenses/travel/new'),
        icon: const Icon(Icons.add_rounded),
        label: const Text(Strings.newTravelRequest),
      ),
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(travelRequestsProvider),
        child: AsyncBody(
          value: travel,
          onRetry: () => ref.invalidate(travelRequestsProvider),
          isEmpty: (items) => items.isEmpty,
          emptyTitle: Strings.noTravel,
          emptyMessage:
              'Travel requests you submit will appear here with their '
              'approval status.',
          emptyIcon: Icons.flight_takeoff_outlined,
          builder: (items) => ListView.separated(
            physics: const AlwaysScrollableScrollPhysics(),
            padding: const EdgeInsets.fromLTRB(16, 16, 16, 88),
            itemCount: items.length,
            separatorBuilder: (_, __) => const SizedBox(height: 10),
            itemBuilder: (context, index) {
              final request = items[index];
              return Card(
                clipBehavior: Clip.antiAlias,
                child: ListTile(
                  leading: const Icon(Icons.flight_takeoff_rounded),
                  title: Text(request.destination),
                  subtitle: Text(
                    [
                      if (request.startDate != null && request.endDate != null)
                        '${AppFormatters.dateShort(request.startDate)} – '
                            '${AppFormatters.date(request.endDate)}',
                      if (request.estimatedCost != null)
                        AppFormatters.currency(
                          request.estimatedCost,
                          request.currency,
                        ),
                      if (request.purpose != null &&
                          request.purpose!.isNotEmpty)
                        request.purpose!,
                    ].join(' · '),
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                  ),
                  trailing: StatusChip(status: request.status, dense: true),
                ),
              );
            },
          ),
        ),
      ),
    );
  }
}
