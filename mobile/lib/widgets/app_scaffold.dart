import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../providers/notifications_provider.dart';

/// App-bar notifications bell with an unread-count badge.
/// Tapping opens the notification inbox (deep links resolve inside it).
class NotificationsBell extends ConsumerWidget {
  const NotificationsBell({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final unread = ref.watch(unreadCountProvider);
    final count = unread.value ?? 0;
    return IconButton(
      onPressed: () => context.go('/home/notifications'),
      tooltip: 'Notifications${count > 0 ? ' ($count unread)' : ''}',
      icon: Badge(
        isLabelVisible: count > 0,
        label: Text(count > 99 ? '99+' : '$count'),
        child: const Icon(Icons.notifications_outlined),
      ),
    );
  }
}

/// Consistent screen scaffold: app bar (with optional bell + back), body.
class AppScaffold extends StatelessWidget {
  const AppScaffold({
    super.key,
    required this.title,
    required this.body,
    this.actions = const [],
    this.floatingActionButton,
    this.bottom,
    this.showBell = true,
    this.leading,
    this.extendBody = false,
  });

  final String title;
  final Widget body;
  final List<Widget> actions;
  final Widget? floatingActionButton;
  final PreferredSizeWidget? bottom;

  /// Show the notifications bell (tab roots and screens without custom
  /// leading). Detail screens pushed by the router get an automatic back
  /// button instead.
  final bool showBell;
  final Widget? leading;
  final bool extendBody;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      extendBody: extendBody,
      appBar: AppBar(
        title: Text(title),
        leading: leading,
        actions: [...actions, if (showBell) const NotificationsBell()],
        bottom: bottom,
      ),
      body: body,
      floatingActionButton: floatingActionButton,
    );
  }
}
