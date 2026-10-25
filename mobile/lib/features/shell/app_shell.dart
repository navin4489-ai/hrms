import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/network/chat_realtime.dart';
import '../../core/network/push_notifications.dart';
import '../../core/strings.dart';
import '../../providers/auth_provider.dart';
import '../../providers/chat_provider.dart';
import '../../providers/approvals_provider.dart';

/// Bottom-navigation shell hosting the five primary branches:
/// Home, Attendance, Leave, Approvals, More.
///
/// Shows an unread badge on the Approvals tab when there are pending tasks
/// (line-manager view; employees see 0). Also hosts the global incoming-call
/// listener so employees are notified of calls anywhere in the app.
class AppShell extends ConsumerStatefulWidget {
  const AppShell({super.key, required this.shell});

  final StatefulNavigationShell shell;

  @override
  ConsumerState<AppShell> createState() => _AppShellState();
}

class _AppShellState extends ConsumerState<AppShell> {
  StreamSubscription<IncomingCall>? _callSub;
  StreamSubscription<Map<String, dynamic>>? _messageSub;
  StreamSubscription<PushCall>? _pushCallSub;
  StreamSubscription<String>? _openConvSub;
  StreamSubscription<String>? _tokenSub;
  bool _dialogOpen = false;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _initRealtime());
  }

  Future<void> _initRealtime() async {
    final auth = ref.read(authStateProvider);
    final canChat = auth.hasPermission('chat.use');
    final canCall = auth.hasPermission('call.use');
    if (!canChat && !canCall) return;

    final rt = ref.read(chatRealtimeProvider);
    rt.connect();
    if (canChat) {
      // Ring the bell + refresh badges when a chat message arrives while the app is open.
      _messageSub = rt.messages.listen((_) {
        PushNotifications.instance.playNotificationSound();
        if (mounted) {
          ref.invalidate(chatUnreadProvider);
        }
      });
    }
    if (canCall) {
      _callSub = rt.incomingCall.listen(_onIncomingCall);
      // Calls arriving via push (app backgrounded) raise the same ringing UI.
      _pushCallSub = PushNotifications.instance.incomingCall.listen(
        (p) => _onIncomingCall(
          IncomingCall(
            callId: p.callId,
            fromEmployeeId: p.fromEmployeeId,
            fromName: p.fromName,
            kind: p.kind,
          ),
        ),
      );
    }
    // Tapping a message notification opens the conversation.
    _openConvSub = PushNotifications.instance.openConversation.listen((convId) {
      if (mounted) {
        context.push('/chat/conversation/$convId', extra: {'title': 'Chat'});
      }
    });

    // Register this device's FCM token so the server can push to it.
    await _registerPushToken();
    _tokenSub = PushNotifications.instance.onTokenRefresh.listen((t) {
      ref.read(chatActionsProvider).registerDevice(t).catchError((_) {});
    });
  }

  Future<void> _registerPushToken() async {
    try {
      final token = await PushNotifications.instance.getToken();
      if (token != null && token.isNotEmpty) {
        await ref.read(chatActionsProvider).registerDevice(token);
      }
    } catch (_) {
      // Push not configured / offline — in-app realtime still works.
    }
  }

  Future<void> _onIncomingCall(IncomingCall call) async {
    if (_dialogOpen || !mounted) return;
    _dialogOpen = true;
    // Ring while the incoming-call dialog is shown.
    PushNotifications.instance.startRingtone();
    final accept = await showDialog<bool>(
      context: context,
      barrierDismissible: false,
      builder: (ctx) => AlertDialog(
        title: Text('Incoming ${call.kind.toLowerCase()} call'),
        content: Text('${call.fromName} is calling you.'),
        actions: [
          TextButton.icon(
            onPressed: () => Navigator.pop(ctx, false),
            icon: const Icon(Icons.call_end, color: Colors.red),
            label: const Text('Decline'),
          ),
          FilledButton.icon(
            onPressed: () => Navigator.pop(ctx, true),
            icon: const Icon(Icons.call),
            label: const Text('Accept'),
          ),
        ],
      ),
    );
    _dialogOpen = false;
    // Stop the dialog ringtone; the call screen manages its own ring afterwards.
    await PushNotifications.instance.stopRingtone();
    if (!mounted) return;
    if (accept == true) {
      context.push(
        '/chat/call',
        extra: {
          'employeeId': call.fromEmployeeId,
          'name': call.fromName,
          'kind': call.kind,
          'outgoing': false,
          'callId': call.callId,
        },
      );
    } else {
      // Decline: end the call on the server.
      try {
        await ref.read(chatActionsProvider).endCall(call.callId, 'Declined');
      } catch (_) {}
    }
  }

  @override
  void dispose() {
    _callSub?.cancel();
    _messageSub?.cancel();
    _pushCallSub?.cancel();
    _openConvSub?.cancel();
    _tokenSub?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final stats = ref.watch(approvalStatsProvider);
    final pending = stats.value?.pending ?? 0;

    return Scaffold(
      body: widget.shell,
      bottomNavigationBar: NavigationBar(
        selectedIndex: widget.shell.currentIndex,
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
    if (index == widget.shell.currentIndex) {
      widget.shell.goBranch(index, initialLocation: true);
    } else {
      widget.shell.goBranch(index);
    }
  }
}
