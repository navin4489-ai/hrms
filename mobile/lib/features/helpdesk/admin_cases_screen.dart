import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/network/api_exception.dart';
import '../../core/strings.dart';
import '../../core/utils/formatters.dart';
import '../../providers/cases_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';
import '../../widgets/status_chip.dart';

/// Agent/HR helpdesk queue (FR-ADM-120..123): browse queues, filter by status
/// and priority, open a case, add internal notes and resolve/close.
class AdminCasesScreen extends ConsumerStatefulWidget {
  const AdminCasesScreen({super.key});

  @override
  ConsumerState<AdminCasesScreen> createState() => _AdminCasesScreenState();
}

class _AdminCasesScreenState extends ConsumerState<AdminCasesScreen> {
  static const _queues = [
    'HR',
    'Payroll',
    'IT',
    'Facilities',
    'General HR',
    'Restricted',
  ];
  String? _queue;
  String? _status;
  String? _priority;

  CaseQuery get _query => CaseQuery(
    mine: false,
    queue: _queue,
    status: _status,
    priority: _priority,
  );

  @override
  Widget build(BuildContext context) {
    final cases = ref.watch(casesProvider(_query));
    return AppScaffold(
      title: 'Helpdesk queue',
      body: Column(
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 6),
            child: Column(
              children: [
                LayoutBuilder(
                  builder: (context, constraints) {
                    final queueField = DropdownButtonFormField<String?>(
                      initialValue: _queue,
                      isExpanded: true,
                      decoration: const InputDecoration(
                        labelText: 'Queue',
                        isDense: true,
                      ),
                      items: [
                        const DropdownMenuItem<String?>(
                          value: null,
                          child: Text('All queues'),
                        ),
                        for (final q in _queues)
                          DropdownMenuItem<String?>(value: q, child: Text(q)),
                      ],
                      onChanged: (v) => setState(() => _queue = v),
                    );
                    final statusField = DropdownButtonFormField<String?>(
                      initialValue: _status,
                      isExpanded: true,
                      decoration: const InputDecoration(
                        labelText: 'Status',
                        isDense: true,
                      ),
                      items: const [
                        DropdownMenuItem<String?>(
                          value: null,
                          child: Text('All'),
                        ),
                        DropdownMenuItem(value: 'Open', child: Text('Open')),
                        DropdownMenuItem(
                          value: 'InProgress',
                          child: Text('In progress'),
                        ),
                        DropdownMenuItem(
                          value: 'Resolved',
                          child: Text('Resolved'),
                        ),
                        DropdownMenuItem(
                          value: 'Closed',
                          child: Text('Closed'),
                        ),
                        DropdownMenuItem(
                          value: 'Reopened',
                          child: Text('Reopened'),
                        ),
                      ],
                      onChanged: (v) => setState(() => _status = v),
                    );
                    final priorityField = DropdownButtonFormField<String?>(
                      initialValue: _priority,
                      isExpanded: true,
                      decoration: const InputDecoration(
                        labelText: 'Priority',
                        isDense: true,
                      ),
                      items: const [
                        DropdownMenuItem<String?>(
                          value: null,
                          child: Text('All'),
                        ),
                        DropdownMenuItem(value: 'Low', child: Text('Low')),
                        DropdownMenuItem(
                          value: 'Medium',
                          child: Text('Medium'),
                        ),
                        DropdownMenuItem(value: 'High', child: Text('High')),
                        DropdownMenuItem(
                          value: 'Urgent',
                          child: Text('Urgent'),
                        ),
                      ],
                      onChanged: (v) => setState(() => _priority = v),
                    );

                    if (constraints.maxWidth < 560) {
                      return Column(
                        children: [
                          queueField,
                          const SizedBox(height: 8),
                          Row(
                            children: [
                              Expanded(child: statusField),
                              const SizedBox(width: 10),
                              Expanded(child: priorityField),
                            ],
                          ),
                        ],
                      );
                    }

                    return Row(
                      children: [
                        Expanded(child: queueField),
                        const SizedBox(width: 10),
                        Expanded(child: statusField),
                        const SizedBox(width: 10),
                        Expanded(child: priorityField),
                      ],
                    );
                  },
                ),
              ],
            ),
          ),
          Expanded(
            child: RefreshIndicator(
              onRefresh: () async => ref.invalidate(casesProvider(_query)),
              child: AsyncBody(
                value: cases,
                onRetry: () => ref.invalidate(casesProvider(_query)),
                isEmpty: (page) => page.items.isEmpty,
                emptyTitle: 'No cases',
                emptyMessage: 'No cases match these filters.',
                emptyIcon: Icons.support_agent_outlined,
                builder: (page) => ListView.separated(
                  physics: const AlwaysScrollableScrollPhysics(),
                  padding: const EdgeInsets.fromLTRB(16, 4, 16, 20),
                  itemCount: page.items.length,
                  separatorBuilder: (_, _) => const SizedBox(height: 8),
                  itemBuilder: (context, index) {
                    final c = page.items[index];
                    return Card(
                      child: ListTile(
                        leading: _priorityIcon(c.priority),
                        title: Text(
                          c.subject,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                        ),
                        subtitle: Text(
                          [
                            c.ticketNumber,
                            if (c.employeeName != null) c.employeeName!,
                            if (c.queue != null) c.queue!,
                            if (c.category != null) c.category!,
                          ].join(' · '),
                          maxLines: 2,
                          overflow: TextOverflow.ellipsis,
                        ),
                        trailing: StatusChip(status: c.status, dense: true),
                        onTap: () => _openCase(c.id),
                      ),
                    );
                  },
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }

  Widget _priorityIcon(String priority) {
    final color = switch (priority) {
      'Urgent' => Colors.red,
      'High' => Colors.orange,
      'Low' => Colors.grey,
      _ => Colors.blue,
    };
    return CircleAvatar(
      backgroundColor: color.withValues(alpha: 0.15),
      child: Icon(Icons.flag_rounded, color: color, size: 18),
    );
  }

  Future<void> _openCase(String id) async {
    await showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => _AdminCaseDetailSheet(
        caseId: id,
        onChanged: () => ref.invalidate(casesProvider(_query)),
      ),
    );
  }
}

class _AdminCaseDetailSheet extends ConsumerStatefulWidget {
  const _AdminCaseDetailSheet({required this.caseId, required this.onChanged});

  final String caseId;
  final VoidCallback onChanged;

  @override
  ConsumerState<_AdminCaseDetailSheet> createState() =>
      _AdminCaseDetailSheetState();
}

class _AdminCaseDetailSheetState extends ConsumerState<_AdminCaseDetailSheet> {
  final _reply = TextEditingController();
  final _note = TextEditingController();
  bool _busy = false;

  @override
  void dispose() {
    _reply.dispose();
    _note.dispose();
    super.dispose();
  }

  void _snack(String message) {
    if (mounted) {
      ScaffoldMessenger.of(context)
          .showSnackBar(SnackBar(content: Text(message)));
    }
  }

  Future<void> _run(Future<void> Function() action, String success) async {
    setState(() => _busy = true);
    try {
      await action();
      ref.invalidate(caseDetailProvider(widget.caseId));
      widget.onChanged();
      _snack(success);
    } on ApiException catch (e) {
      _snack(e.detail ?? e.message);
    } catch (_) {
      _snack(Strings.offlineMessage);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _transition(String action) async {
    String? resolution;
    if (action == 'Resolve') {
      final controller = TextEditingController();
      resolution = await showDialog<String>(
        context: context,
        builder: (dialogContext) => AlertDialog(
          title: const Text('Resolve case'),
          content: TextField(
            controller: controller,
            decoration: const InputDecoration(
              labelText: 'Resolution note (optional)',
            ),
            minLines: 2,
            maxLines: 4,
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(dialogContext),
              child: const Text(Strings.cancel),
            ),
            FilledButton(
              onPressed: () =>
                  Navigator.pop(dialogContext, controller.text.trim()),
              child: const Text('Resolve'),
            ),
          ],
        ),
      );
      controller.dispose();
      if (resolution == null) return;
    }
    await _run(
      () => ref
          .read(caseActionsProvider)
          .transition(
            caseId: widget.caseId,
            action: action,
            comment: resolution,
          ),
      'Case ${action.toLowerCase()}d',
    );
  }

  @override
  Widget build(BuildContext context) {
    final detail = ref.watch(caseDetailProvider(widget.caseId));
    return SafeArea(
      child: SizedBox(
        height: MediaQuery.sizeOf(context).height * .88,
        child: AsyncBody(
          value: detail,
          onRetry: () => ref.invalidate(caseDetailProvider(widget.caseId)),
          builder: (c) => Column(
            children: [
              Expanded(
                child: ListView(
                  padding: const EdgeInsets.all(16),
                  children: [
                    Text(
                      c.subject,
                      style: Theme.of(context).textTheme.titleLarge,
                    ),
                    const SizedBox(height: 4),
                    Wrap(
                      spacing: 8,
                      runSpacing: 4,
                      crossAxisAlignment: WrapCrossAlignment.center,
                      children: [
                        StatusChip(status: c.status, dense: true),
                        Text(c.ticketNumber),
                        Text('· ${c.priority}'),
                        if (c.queue != null) Text('· ${c.queue}'),
                      ],
                    ),
                    const SizedBox(height: 4),
                    Text(
                      [
                        if (c.employeeName != null) 'From: ${c.employeeName}',
                        if (c.category != null) c.category!,
                        if (c.slaDueAt != null)
                          'SLA due: ${AppFormatters.timestamp(c.slaDueAt)}',
                      ].join(' · '),
                      style: Theme.of(context).textTheme.bodySmall,
                    ),
                    if (c.description != null) ...[
                      const SizedBox(height: 12),
                      Text(c.description!),
                    ],
                    const Divider(height: 28),
                    Text(
                      'Conversation',
                      style: Theme.of(context).textTheme.titleMedium,
                    ),
                    const SizedBox(height: 8),
                    for (final m in c.messages)
                      Align(
                        alignment: m.isInternal
                            ? Alignment.center
                            : Alignment.centerLeft,
                        child: Container(
                          margin: const EdgeInsets.symmetric(vertical: 4),
                          padding: const EdgeInsets.all(10),
                          decoration: BoxDecoration(
                            color: m.isInternal
                                ? Colors.amber.withValues(alpha: 0.15)
                                : Theme.of(context)
                                      .colorScheme
                                      .surfaceContainerHighest,
                            borderRadius: BorderRadius.circular(8),
                          ),
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Text(
                                '${m.senderName}${m.isInternal ? ' · internal note' : ''}',
                                style: Theme.of(context).textTheme.labelSmall
                                    ?.copyWith(
                                      fontWeight: FontWeight.w700,
                                      color: m.isInternal ? Colors.brown : null,
                                    ),
                              ),
                              const SizedBox(height: 2),
                              Text(m.body),
                            ],
                          ),
                        ),
                      ),
                  ],
                ),
              ),
              const Divider(height: 1),
              Padding(
                padding: const EdgeInsets.fromLTRB(12, 8, 12, 12),
                child: Column(
                  children: [
                    Row(
                      children: [
                        Expanded(
                          child: TextField(
                            controller: _reply,
                            minLines: 1,
                            maxLines: 3,
                            decoration: const InputDecoration(
                              labelText: 'Reply to employee',
                              isDense: true,
                            ),
                          ),
                        ),
                        IconButton(
                          tooltip: 'Send reply',
                          onPressed: _busy
                              ? null
                              : () {
                                  final body = _reply.text.trim();
                                  if (body.isEmpty) return;
                                  _run(
                                    () => ref
                                        .read(caseActionsProvider)
                                        .reply(
                                          caseId: widget.caseId,
                                          body: body,
                                        ),
                                    'Reply sent',
                                  ).then((_) => _reply.clear());
                                },
                          icon: const Icon(Icons.send_rounded),
                        ),
                      ],
                    ),
                    Row(
                      children: [
                        Expanded(
                          child: TextField(
                            controller: _note,
                            minLines: 1,
                            maxLines: 3,
                            decoration: const InputDecoration(
                              labelText: 'Internal note (hidden from employee)',
                              isDense: true,
                            ),
                          ),
                        ),
                        IconButton(
                          tooltip: 'Add internal note',
                          onPressed: _busy
                              ? null
                              : () {
                                  final body = _note.text.trim();
                                  if (body.isEmpty) return;
                                  _run(
                                    () => ref
                                        .read(caseActionsProvider)
                                        .addNote(
                                          caseId: widget.caseId,
                                          body: body,
                                        ),
                                    'Internal note added',
                                  ).then((_) => _note.clear());
                                },
                          icon: const Icon(Icons.sticky_note_2_outlined),
                        ),
                      ],
                    ),
                    const SizedBox(height: 6),
                    Row(
                      children: [
                        if (c.status != 'Resolved' && c.status != 'Closed')
                          Expanded(
                            child: OutlinedButton.icon(
                              onPressed: _busy
                                  ? null
                                  : () => _transition('Resolve'),
                              icon: const Icon(Icons.check_circle_outline),
                              label: const Text('Resolve'),
                            ),
                          ),
                        if (c.status != 'Closed') ...[
                          const SizedBox(width: 8),
                          Expanded(
                            child: OutlinedButton.icon(
                              onPressed: _busy
                                  ? null
                                  : () => _transition('Close'),
                              icon: const Icon(Icons.lock_outline_rounded),
                              label: const Text('Close'),
                            ),
                          ),
                        ],
                        if (c.status == 'Resolved' || c.status == 'Closed') ...[
                          const SizedBox(width: 8),
                          Expanded(
                            child: OutlinedButton.icon(
                              onPressed: _busy
                                  ? null
                                  : () => _transition('Reopen'),
                              icon: const Icon(Icons.refresh_rounded),
                              label: const Text('Reopen'),
                            ),
                          ),
                        ],
                      ],
                    ),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
