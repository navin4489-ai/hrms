import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/network/chat_realtime.dart';
import '../../core/utils/formatters.dart';
import '../../models/chat_models.dart';
import '../../providers/chat_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';

/// Chat home: conversation list, a new-chat/contact picker and call history.
class ChatHomeScreen extends ConsumerStatefulWidget {
  const ChatHomeScreen({super.key});

  @override
  ConsumerState<ChatHomeScreen> createState() => _ChatHomeScreenState();
}

class _ChatHomeScreenState extends ConsumerState<ChatHomeScreen>
    with SingleTickerProviderStateMixin {
  late final TabController _tabs = TabController(length: 2, vsync: this);
  late final ChatRealtimeBinder _binder;

  @override
  void initState() {
    super.initState();
    // Connect realtime and refresh the list on any inbound message/update.
    final rt = ref.read(chatRealtimeProvider);
    rt.connect();
    _binder = ChatRealtimeBinder(rt, () {
      if (mounted) {
        ref.invalidate(conversationsProvider);
        ref.invalidate(chatUnreadProvider);
      }
    })..bind();
  }

  @override
  void dispose() {
    _binder.unbind();
    _tabs.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return AppScaffold(
      title: 'Chat',
      bottom: TabBar(
        controller: _tabs,
        tabs: const [
          Tab(text: 'Messages', icon: Icon(Icons.forum_outlined)),
          Tab(text: 'Calls', icon: Icon(Icons.call_outlined)),
        ],
      ),
      floatingActionButton: FloatingActionButton(
        onPressed: () => _newChat(context),
        child: const Icon(Icons.edit_outlined),
      ),
      body: TabBarView(
        controller: _tabs,
        children: const [_ConversationsTab(), _CallsTab()],
      ),
    );
  }

  Future<void> _newChat(BuildContext context) async {
    await showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => const _ContactPickerSheet(),
    );
    if (mounted) ref.invalidate(conversationsProvider);
  }
}

/// Helper that subscribes to realtime streams and triggers a callback.
class ChatRealtimeBinder {
  ChatRealtimeBinder(this._rt, this._onChange);
  final ChatRealtime _rt;
  final VoidCallback _onChange;
  final List<Object> _subs = [];

  void bind() {
    _subs.add(_rt.messages.listen((_) => _onChange()));
    _subs.add(_rt.conversationUpdated.listen((_) => _onChange()));
  }

  void unbind() {
    for (final s in _subs) {
      (s as dynamic).cancel();
    }
    _subs.clear();
  }
}

class _ConversationsTab extends ConsumerWidget {
  const _ConversationsTab();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final conversations = ref.watch(conversationsProvider);
    return RefreshIndicator(
      onRefresh: () async => ref.invalidate(conversationsProvider),
      child: AsyncBody(
        value: conversations,
        onRetry: () => ref.invalidate(conversationsProvider),
        isEmpty: (items) => items.isEmpty,
        emptyTitle: 'No conversations yet',
        emptyMessage: 'Tap the pencil to start chatting with a colleague.',
        emptyIcon: Icons.forum_outlined,
        builder: (items) => ListView.separated(
          physics: const AlwaysScrollableScrollPhysics(),
          itemCount: items.length,
          separatorBuilder: (_, _) => const Divider(height: 1),
          itemBuilder: (context, index) {
            final c = items[index];
            return ListTile(
              leading: CircleAvatar(
                child: Icon(
                  c.isGroup ? Icons.groups_rounded : Icons.person_rounded,
                ),
              ),
              title: Text(
                c.title,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
              ),
              subtitle: Text(
                c.lastMessagePreview ?? 'No messages yet',
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
              ),
              trailing: Column(
                mainAxisAlignment: MainAxisAlignment.center,
                crossAxisAlignment: CrossAxisAlignment.end,
                children: [
                  Text(
                    AppFormatters.time(c.lastMessageAt),
                    style: Theme.of(context).textTheme.labelSmall,
                  ),
                  if (c.unreadCount > 0)
                    Container(
                      margin: const EdgeInsets.only(top: 4),
                      padding: const EdgeInsets.all(6),
                      decoration: BoxDecoration(
                        color: Theme.of(context).colorScheme.primary,
                        shape: BoxShape.circle,
                      ),
                      child: Text(
                        '${c.unreadCount}',
                        style: const TextStyle(
                          color: Colors.white,
                          fontSize: 11,
                        ),
                      ),
                    ),
                ],
              ),
              onTap: () => context.push(
                '/chat/conversation/${c.id}',
                extra: {
                  'title': c.title,
                  'otherEmployeeId': c.otherEmployeeId,
                  'isGroup': c.isGroup,
                },
              ),
            );
          },
        ),
      ),
    );
  }
}

class _CallsTab extends ConsumerWidget {
  const _CallsTab();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final history = ref.watch(callHistoryProvider);
    return RefreshIndicator(
      onRefresh: () async => ref.invalidate(callHistoryProvider),
      child: AsyncBody(
        value: history,
        onRetry: () => ref.invalidate(callHistoryProvider),
        isEmpty: (items) => items.isEmpty,
        emptyTitle: 'No calls yet',
        emptyMessage: 'Your call history will appear here.',
        emptyIcon: Icons.call_outlined,
        builder: (items) => ListView.separated(
          physics: const AlwaysScrollableScrollPhysics(),
          itemCount: items.length,
          separatorBuilder: (_, _) => const Divider(height: 1),
          itemBuilder: (context, index) {
            final call = items[index];
            final color = call.isMissed ? Colors.red : null;
            return ListTile(
              leading: Icon(
                call.outgoing ? Icons.call_made : Icons.call_received,
                color: color,
              ),
              title: Text(call.otherName),
              subtitle: Text(
                '${call.isVideo ? 'Video' : 'Audio'} · ${call.state}'
                '${call.durationSeconds != null ? ' · ${_fmtDuration(call.durationSeconds!)}' : ''}',
                style: TextStyle(color: color),
              ),
              trailing: Text(
                AppFormatters.dateShort(call.startedAt),
                style: Theme.of(context).textTheme.labelSmall,
              ),
              onTap: () => context.push(
                '/chat/call',
                extra: {
                  'employeeId': call.otherEmployeeId,
                  'name': call.otherName,
                  'kind': call.kind,
                  'outgoing': true,
                },
              ),
            );
          },
        ),
      ),
    );
  }
}

String _fmtDuration(int seconds) {
  final m = seconds ~/ 60;
  final s = seconds % 60;
  return '$m:${s.toString().padLeft(2, '0')}';
}

class _ContactPickerSheet extends ConsumerStatefulWidget {
  const _ContactPickerSheet();

  @override
  ConsumerState<_ContactPickerSheet> createState() =>
      _ContactPickerSheetState();
}

class _ContactPickerSheetState extends ConsumerState<_ContactPickerSheet> {
  String _search = '';
  bool _groupMode = false;
  final Set<String> _selected = {};
  final _groupName = TextEditingController();
  bool _busy = false;

  @override
  void dispose() {
    _groupName.dispose();
    super.dispose();
  }

  Future<void> _startDirect(ChatContact contact) async {
    setState(() => _busy = true);
    try {
      final conv = await ref
          .read(chatActionsProvider)
          .startDirect(contact.employeeId);
      if (mounted) {
        Navigator.pop(context);
        context.push(
          '/chat/conversation/${conv.id}',
          extra: {
            'title': contact.displayName,
            'otherEmployeeId': contact.employeeId,
            'isGroup': false,
          },
        );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _createGroup() async {
    if (_groupName.text.trim().isEmpty || _selected.isEmpty) return;
    setState(() => _busy = true);
    try {
      final conv = await ref
          .read(chatActionsProvider)
          .createGroup(_groupName.text.trim(), _selected.toList());
      if (mounted) {
        Navigator.pop(context);
        context.push(
          '/chat/conversation/${conv.id}',
          extra: {'title': conv.title, 'isGroup': true},
        );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final contacts = ref.watch(chatContactsProvider(_search));
    return SafeArea(
      child: SizedBox(
        height: MediaQuery.sizeOf(context).height * .85,
        child: Column(
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 8, 16, 4),
              child: Row(
                children: [
                  Text(
                    _groupMode ? 'New group' : 'New chat',
                    style: Theme.of(context).textTheme.titleLarge,
                  ),
                  const Spacer(),
                  TextButton.icon(
                    onPressed: () => setState(() {
                      _groupMode = !_groupMode;
                      _selected.clear();
                    }),
                    icon: Icon(_groupMode ? Icons.person : Icons.groups),
                    label: Text(_groupMode ? 'Direct' : 'Group'),
                  ),
                ],
              ),
            ),
            if (_groupMode)
              Padding(
                padding: const EdgeInsets.symmetric(horizontal: 16),
                child: TextField(
                  controller: _groupName,
                  decoration: const InputDecoration(
                    labelText: 'Group name',
                    isDense: true,
                  ),
                ),
              ),
            Padding(
              padding: const EdgeInsets.all(16),
              child: TextField(
                decoration: const InputDecoration(
                  hintText: 'Search colleagues',
                  prefixIcon: Icon(Icons.search),
                  isDense: true,
                ),
                onChanged: (v) => setState(() => _search = v),
              ),
            ),
            Expanded(
              child: AsyncBody(
                value: contacts,
                onRetry: () => ref.invalidate(chatContactsProvider(_search)),
                isEmpty: (items) => items.isEmpty,
                emptyTitle: 'No colleagues found',
                emptyMessage: 'Try a different search.',
                emptyIcon: Icons.search_off,
                builder: (items) => ListView.builder(
                  itemCount: items.length,
                  itemBuilder: (context, index) {
                    final c = items[index];
                    final selected = _selected.contains(c.employeeId);
                    return ListTile(
                      leading: CircleAvatar(
                        child: Text(
                          c.displayName.isNotEmpty ? c.displayName[0] : '?',
                        ),
                      ),
                      title: Text(c.displayName),
                      subtitle: Text(
                        [
                          c.designation,
                          c.department,
                        ].whereType<String>().join(' · '),
                      ),
                      trailing: _groupMode
                          ? Checkbox(
                              value: selected,
                              onChanged: (v) => setState(() {
                                if (v == true) {
                                  _selected.add(c.employeeId);
                                } else {
                                  _selected.remove(c.employeeId);
                                }
                              }),
                            )
                          : null,
                      onTap: _busy
                          ? null
                          : () {
                              if (_groupMode) {
                                setState(() {
                                  if (selected) {
                                    _selected.remove(c.employeeId);
                                  } else {
                                    _selected.add(c.employeeId);
                                  }
                                });
                              } else {
                                _startDirect(c);
                              }
                            },
                    );
                  },
                ),
              ),
            ),
            if (_groupMode)
              Padding(
                padding: const EdgeInsets.all(12),
                child: FilledButton.icon(
                  onPressed: _busy || _selected.isEmpty ? null : _createGroup,
                  icon: const Icon(Icons.check),
                  label: Text('Create group (${_selected.length})'),
                ),
              ),
          ],
        ),
      ),
    );
  }
}
