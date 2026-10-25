import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/strings.dart';
import '../../providers/api_provider.dart';
import '../../providers/directory_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';

final adminLifecycleChecklistsProvider = FutureProvider<Map<String, dynamic>>(
  (ref) async => ref
      .watch(apiClientProvider)
      .getMap('/lifecycle/checklists', query: {'page': 1, 'pageSize': 100}),
);

final adminLifecycleTemplatesProvider = FutureProvider<List<dynamic>>(
  (ref) async => ref.watch(apiClientProvider).getList('/lifecycle/templates'),
);

final adminLifecycleDetailProvider =
    FutureProvider.family<Map<String, dynamic>, String>(
      (ref, id) =>
          ref.watch(apiClientProvider).getMap('/lifecycle/checklists/$id'),
    );

class AdminLifecycleScreen extends ConsumerStatefulWidget {
  const AdminLifecycleScreen({super.key});

  @override
  ConsumerState<AdminLifecycleScreen> createState() =>
      _AdminLifecycleScreenState();
}

class _AdminLifecycleScreenState extends ConsumerState<AdminLifecycleScreen> {
  String _search = '';

  Future<void> _createChecklist() async {
    final options = await ref.read(employeeCreateOptionsProvider.future);
    final templates = await ref.read(adminLifecycleTemplatesProvider.future);
    if (!mounted) return;
    if (options.managers.isEmpty || templates.isEmpty) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text(
            'An active employee and checklist template are required.',
          ),
        ),
      );
      return;
    }

    String? employeeId;
    String? templateCode = (templates.first as Map)['code']?.toString();
    var startDate = DateTime.now().toIso8601String().substring(0, 10);
    final selected = await showDialog<Map<String, dynamic>>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) => AlertDialog(
          title: const Text('Create onboarding / offboarding checklist'),
          content: SizedBox(
            width: 460,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                DropdownButtonFormField<String>(
                  initialValue: employeeId,
                  decoration: const InputDecoration(labelText: 'Employee'),
                  items: [
                    for (final employee in options.managers)
                      DropdownMenuItem(
                        value: employee.employeeId,
                        child: Text(employee.label),
                      ),
                  ],
                  onChanged: (value) =>
                      setDialogState(() => employeeId = value),
                ),
                const SizedBox(height: 12),
                DropdownButtonFormField<String>(
                  initialValue: templateCode,
                  decoration: const InputDecoration(
                    labelText: 'Checklist template',
                  ),
                  items: [
                    for (final raw in templates.whereType<Map>())
                      DropdownMenuItem<String>(
                        value: raw['code']?.toString(),
                        child: Text('${raw['name']} · ${raw['type']}'),
                      ),
                  ],
                  onChanged: (value) =>
                      setDialogState(() => templateCode = value),
                ),
                const SizedBox(height: 12),
                TextFormField(
                  initialValue: startDate,
                  decoration: const InputDecoration(
                    labelText: 'Start date · YYYY-MM-DD',
                  ),
                  onChanged: (value) => startDate = value.trim(),
                ),
              ],
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(dialogContext),
              child: const Text(Strings.cancel),
            ),
            FilledButton(
              onPressed: employeeId == null || templateCode == null
                  ? null
                  : () {
                      final selectedTemplate = templates
                          .whereType<Map>()
                          .firstWhere(
                            (row) => row['code']?.toString() == templateCode,
                          );
                      Navigator.pop(dialogContext, {
                        'employeeId': employeeId,
                        'type': selectedTemplate['type'],
                        'templateCode': templateCode,
                        'startDate': startDate,
                      });
                    },
              child: const Text('Create checklist'),
            ),
          ],
        ),
      ),
    );
    if (selected == null || !mounted) return;
    try {
      await ref
          .read(apiClientProvider)
          .postMap('/lifecycle/checklists', data: selected);
      ref.invalidate(adminLifecycleChecklistsProvider);
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(const SnackBar(content: Text('Checklist created')));
      }
    } catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Checklist could not be created: $error')),
        );
      }
    }
  }

  Future<void> _openChecklist(Map<String, dynamic> item) async {
    final id = item['id']?.toString();
    if (id == null) return;
    await showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (context) => _LifecycleDetailSheet(checklistId: id),
    );
  }

  @override
  Widget build(BuildContext context) {
    final checklists = ref.watch(adminLifecycleChecklistsProvider);
    return AppScaffold(
      title: 'Onboarding & offboarding',
      floatingActionButton: FloatingActionButton.extended(
        onPressed: _createChecklist,
        icon: const Icon(Icons.playlist_add_rounded),
        label: const Text('Create checklist'),
      ),
      body: Column(
        children: [
          Padding(
            padding: const EdgeInsets.all(16),
            child: TextField(
              decoration: const InputDecoration(
                labelText: 'Search checklists',
                prefixIcon: Icon(Icons.search_rounded),
              ),
              onSubmitted: (value) => setState(() => _search = value.trim()),
            ),
          ),
          Expanded(
            child: AsyncBody(
              value: checklists,
              onRetry: () => ref.invalidate(adminLifecycleChecklistsProvider),
              isEmpty: (data) => (data['items'] as List? ?? const []).isEmpty,
              emptyTitle: 'No lifecycle checklists',
              emptyMessage: 'Create an onboarding or offboarding checklist.',
              emptyIcon: Icons.checklist_rounded,
              builder: (data) {
                final rows = data['items'] is List
                    ? (data['items'] as List).whereType<Map>().toList()
                    : <Map>[];
                final visible = rows.where((row) {
                  final text =
                      '${row['employeeName']} ${row['employeeNumber']} ${row['type']}'
                          .toLowerCase();
                  return text.contains(_search.toLowerCase());
                }).toList();
                return ListView.separated(
                  physics: const AlwaysScrollableScrollPhysics(),
                  padding: const EdgeInsets.fromLTRB(16, 0, 16, 90),
                  itemCount: visible.length,
                  separatorBuilder: (_, _) => const SizedBox(height: 8),
                  itemBuilder: (context, index) {
                    final item = Map<String, dynamic>.from(visible[index]);
                    final progress =
                        (item['progressPercent'] as num?)?.toDouble() ?? 0;
                    return Card(
                      child: ListTile(
                        leading: SizedBox(
                          width: 42,
                          height: 42,
                          child: CircularProgressIndicator(
                            value: progress / 100,
                          ),
                        ),
                        title: Text(
                          '${item['employeeName'] ?? 'Employee'} · ${item['type'] ?? ''}',
                        ),
                        subtitle: Text(
                          '${item['employeeNumber'] ?? ''} · ${item['stage'] ?? ''}\n'
                          '${item['tasksCompleted'] ?? 0}/${item['tasksTotal'] ?? 0} tasks · '
                          '${item['overdueCount'] ?? 0} overdue',
                        ),
                        isThreeLine: true,
                        trailing: const Icon(Icons.chevron_right_rounded),
                        onTap: () => _openChecklist(item),
                      ),
                    );
                  },
                );
              },
            ),
          ),
        ],
      ),
    );
  }
}

class _LifecycleDetailSheet extends ConsumerWidget {
  const _LifecycleDetailSheet({required this.checklistId});

  final String checklistId;

  Future<void> _transition(
    BuildContext context,
    WidgetRef ref,
    Map<String, dynamic> task,
    String action,
  ) async {
    final controller = TextEditingController();
    final note = await showDialog<String>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: Text('$action task'),
        content: TextField(
          controller: controller,
          minLines: 2,
          maxLines: 4,
          decoration: const InputDecoration(labelText: 'Note (optional)'),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(dialogContext),
            child: const Text(Strings.cancel),
          ),
          FilledButton(
            onPressed: () =>
                Navigator.pop(dialogContext, controller.text.trim()),
            child: Text(action),
          ),
        ],
      ),
    );
    controller.dispose();
    if (note == null || !context.mounted) return;
    try {
      await ref
          .read(apiClientProvider)
          .postMap(
            '/lifecycle/checklists/$checklistId/tasks/${task['id']}',
            data: {'action': action, 'note': note},
          );
      ref.invalidate(adminLifecycleDetailProvider(checklistId));
      ref.invalidate(adminLifecycleChecklistsProvider);
    } catch (error) {
      if (context.mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text('Task update failed: $error')));
      }
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final detail = ref.watch(adminLifecycleDetailProvider(checklistId));
    return SafeArea(
      child: SizedBox(
        height: MediaQuery.sizeOf(context).height * .82,
        child: AsyncBody(
          value: detail,
          onRetry: () =>
              ref.invalidate(adminLifecycleDetailProvider(checklistId)),
          builder: (data) {
            final tasks = data['tasks'] is List
                ? (data['tasks'] as List).whereType<Map>().toList()
                : <Map>[];
            final audit = data['auditTrail'] is List
                ? (data['auditTrail'] as List).whereType<Map>().toList()
                : <Map>[];
            return ListView(
              padding: const EdgeInsets.all(16),
              children: [
                Text(
                  '${data['employeeName'] ?? 'Checklist'} · ${data['type'] ?? ''}',
                  style: Theme.of(context).textTheme.titleLarge,
                ),
                const SizedBox(height: 12),
                for (final rawTask in tasks)
                  _LifecycleTaskCard(
                    task: Map<String, dynamic>.from(rawTask),
                    onAction: (task, action) =>
                        _transition(context, ref, task, action),
                  ),
                const SizedBox(height: 18),
                Text(
                  'Audit history',
                  style: Theme.of(context).textTheme.titleMedium,
                ),
                for (final item in audit)
                  ListTile(
                    dense: true,
                    title: Text(
                      '${item['action'] ?? ''} · ${item['actorName'] ?? ''}',
                    ),
                    subtitle: Text(
                      '${item['at'] ?? ''}${item['note'] == null ? '' : ' · ${item['note']}'}',
                    ),
                  ),
              ],
            );
          },
        ),
      ),
    );
  }
}

class _LifecycleTaskCard extends StatelessWidget {
  const _LifecycleTaskCard({required this.task, required this.onAction});

  final Map<String, dynamic> task;
  final Future<void> Function(Map<String, dynamic>, String) onAction;

  @override
  Widget build(BuildContext context) {
    final status = task['status']?.toString() ?? 'NotStarted';
    final action = switch (status) {
      'NotStarted' || 'Overdue' => 'Start',
      'InProgress' => 'Complete',
      'Completed' || 'Waived' => 'Reopen',
      _ => null,
    };
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              task['title']?.toString() ?? 'Task',
              style: Theme.of(context).textTheme.titleSmall,
            ),
            Text(
              '${task['owner'] ?? ''} · $status · due ${task['dueDate'] ?? '—'}',
            ),
            if (task['description'] != null)
              Text(task['description'].toString()),
            Row(
              mainAxisAlignment: MainAxisAlignment.end,
              children: [
                if (status == 'NotStarted' ||
                    status == 'InProgress' ||
                    status == 'Overdue')
                  TextButton(
                    onPressed: () => onAction(task, 'Waive'),
                    child: const Text('Waive'),
                  ),
                if (action != null)
                  FilledButton.tonal(
                    onPressed: () => onAction(task, action),
                    child: Text(action),
                  ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}
