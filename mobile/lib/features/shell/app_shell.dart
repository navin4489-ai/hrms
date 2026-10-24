import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/strings.dart';
import '../../providers/approvals_provider.dart';
import '../../widgets/app_scaffold.dart';

/// Bottom-navigation shell hosting the five primary branches:
/// Home, Attendance, Leave, Approvals, More.
///
/// Shows an unread badge on the Approvals tab when there are pending tasks
/// (line-manager view; employees see 0).
class AppShell extends ConsumerWidget {
  const AppShell({super.key, required this.shell});

  final StatefulNavigationShell shell;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final stats = ref.watch(approvalStatsProvider);
    final pending = stats.value?.pending ?? 0;

    return Scaffold(
      body: shell,
      bottomNavigationBar: NavigationBar(
        selectedIndex: shell.currentIndex,
        onDestinationSelected: _onSelect,
        destinations: [
          const NavigationDestination(
            icon: Icon(Icons.home_outlined),
            selectedIcon: Icon(Icons.home_rounded),
            label: Strings.tabHome,
          ),
          const NavigationDestination(
            icon: Icon(Icons.calendar_month_outlined),
            selectedIcon: Icon(Icons.calendar_month_rounded),
            label: Strings.tabAttendance,
          ),
          const NavigationDestination(
            icon: Icon(Icons.beach_access_outlined),
            selectedIcon: Icon(Icons.beach_access_rounded),
            label: Strings.tabLeave,
          ),
          NavigationDestination(
            icon: Badge(
              isLabelVisible: pending > 0,
              label: Text(pending > 99 ? '99+' : '$pending'),
              child: const Icon(Icons.how_to_reg_outlined),
            ),
            selectedIcon: Badge(
              isLabelVisible: pending > 0,
              label: Text(pending > 99 ? '99+' : '$pending'),
              child: const Icon(Icons.how_to_reg_rounded),
            ),
            label: Strings.tabApprovals,
          ),
          const NavigationDestination(
            icon: Icon(Icons.grid_view_outlined),
            selectedIcon: Icon(Icons.grid_view_rounded),
            label: Strings.tabMore,
          ),
        ],
      ),
    );
  }

  void _onSelect(int index) {
    // Tapping the active tab pops its stack to the root (Material pattern).
    if (index == shell.currentIndex) {
      shell.goBranch(index, initialLocation: true);
    } else {
      shell.goBranch(index);
    }
  }
}
