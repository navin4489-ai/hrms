import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/strings.dart';
import '../../providers/api_provider.dart';
import '../../providers/auth_provider.dart';
import '../../providers/performance_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';

final adminPerformanceCyclesProvider = FutureProvider<List<dynamic>>(
  (ref) => ref.watch(apiClientProvider).getList('/admin/performance-cycles'),
);
final adminTrainingCoursesProvider = FutureProvider<List<dynamic>>(
  (ref) => ref.watch(apiClientProvider).getList('/admin/training/courses'),
);

class AdminPerformanceScreen extends ConsumerWidget {
  const AdminPerformanceScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final auth = ref.watch(authStateProvider);
    final tabs = <Tab>[];
    final pages = <Widget>[];
    if (auth.hasPermission('performance.manage')) {
      tabs.add(const Tab(text: 'Review cycles'));
      pages.add(const _PerformanceCyclesTab());
    }
    if (auth.hasPermission('training.manage')) {
      tabs.add(const Tab(text: 'Training catalogue'));
      pages.add(const _TrainingCoursesTab());
    }
    if (tabs.isEmpty) {
      return const AppScaffold(
        title: 'Performance & learning',
        body: Center(
          child: Text(
            'You do not have performance or training admin permissions.',
          ),
        ),
      );
    }
    return DefaultTabController(
      length: tabs.length,
      child: AppScaffold(
        title: 'Performance & learning',
        bottom: TabBar(isScrollable: true, tabs: tabs),
        body: TabBarView(children: pages),
      ),
    );
  }
}

class _PerformanceCyclesTab extends ConsumerWidget {
  const _PerformanceCyclesTab();

  Future<void> _edit(
    BuildContext context,
    WidgetRef ref, {
    Map<String, dynamic>? current,
  }) async {
    final name = TextEditingController(
      text: current?['name']?.toString() ?? '',
    );
    final start = TextEditingController(
      text: current?['startDate']?.toString() ?? '',
    );
    final end = TextEditingController(
      text: current?['endDate']?.toString() ?? '',
    );
    final due = TextEditingController(
      text: current?['selfAssessmentDueDate']?.toString() ?? '',
    );
    var status = current?['status']?.toString() ?? 'Upcoming';
    final key = GlobalKey<FormState>();
    final saved = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setState) => AlertDialog(
          title: Text(
            current == null ? 'Create review cycle' : 'Edit review cycle',
          ),
          content: Form(
            key: key,
            child: SingleChildScrollView(
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  _required(name, 'Cycle name'),
                  _required(start, 'Start date · YYYY-MM-DD'),
                  _required(end, 'End date · YYYY-MM-DD'),
                  TextField(
                    controller: due,
                    decoration: const InputDecoration(
                      labelText: 'Self-assessment due date (optional)',
                    ),
                  ),
                  DropdownButtonFormField<String>(
                    initialValue: status,
                    decoration: const InputDecoration(
                      labelText: 'Cycle status',
                    ),
                    items: const [
                      DropdownMenuItem(
                        value: 'Upcoming',
                        child: Text('Upcoming'),
                      ),
                      DropdownMenuItem(value: 'Open', child: Text('Open')),
                      DropdownMenuItem(value: 'Closed', child: Text('Closed')),
                    ],
                    onChanged: (value) =>
                        setState(() => status = value ?? status),
                  ),
                ],
              ),
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(dialogContext),
              child: const Text(Strings.cancel),
            ),
            FilledButton(
              onPressed: () {
                if (key.currentState?.validate() ?? false)
                  Navigator.pop(dialogContext, true);
              },
              child: const Text(Strings.save),
            ),
          ],
        ),
      ),
    );
    if (saved == true && context.mounted) {
      try {
        final data = {
          'name': name.text.trim(),
          'startDate': start.text.trim(),
          'endDate': end.text.trim(),
          'selfAssessmentDueDate': due.text.trim().isEmpty
              ? null
              : due.text.trim(),
          'status': status,
        };
        final api = ref.read(apiClientProvider);
        if (current == null) {
          await api.postMap('/admin/performance-cycles', data: data);
        } else {
          await api.putMap(
            '/admin/performance-cycles/${current['id']}',
            data: data,
          );
        }
        ref.invalidate(adminPerformanceCyclesProvider);
        if (context.mounted)
          ScaffoldMessenger.of(context).showSnackBar(
            const SnackBar(content: Text(Strings.savedSuccessfully)),
          );
      } catch (error) {
        if (context.mounted)
          ScaffoldMessenger.of(context).showSnackBar(
            SnackBar(content: Text('Cycle update failed: $error')),
          );
      }
    }
    for (final controller in [name, start, end, due]) {
      controller.dispose();
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final cycles = ref.watch(adminPerformanceCyclesProvider);
    return Column(
      children: [
        _add('Create cycle', () => _edit(context, ref)),
        Expanded(
          child: AsyncBody(
            value: cycles,
            onRetry: () => ref.invalidate(adminPerformanceCyclesProvider),
            isEmpty: (items) => items.isEmpty,
            emptyTitle: 'No review cycles',
            emptyMessage: 'Create a cycle and set its assessment window.',
            builder: (items) => ListView.separated(
              padding: const EdgeInsets.all(12),
              itemCount: items.length,
              separatorBuilder: (_, _) => const SizedBox(height: 8),
              itemBuilder: (context, index) {
                final item = Map<String, dynamic>.from(items[index] as Map);
                return Card(
                  child: ListTile(
                    title: Text(item['name']?.toString() ?? 'Cycle'),
                    subtitle: Text(
                      '${item['startDate']} – ${item['endDate']} · ${item['submittedAssessments'] ?? 0}/${item['employees'] ?? 0} submitted · due ${item['selfAssessmentDueDate'] ?? '—'}',
                    ),
                    isThreeLine: true,
                    trailing: Column(
                      mainAxisAlignment: MainAxisAlignment.center,
                      children: [
                        Text(item['status']?.toString() ?? ''),
                        Icon(
                          Icons.edit_outlined,
                          color: Theme.of(context).colorScheme.primary,
                        ),
                      ],
                    ),
                    onTap: () => _edit(context, ref, current: item),
                  ),
                );
              },
            ),
          ),
        ),
      ],
    );
  }
}

class _TrainingCoursesTab extends ConsumerWidget {
  const _TrainingCoursesTab();

  Future<void> _edit(
    BuildContext context,
    WidgetRef ref, {
    Map<String, dynamic>? current,
  }) async {
    final title = TextEditingController(
      text: current?['title']?.toString() ?? '',
    );
    final description = TextEditingController(
      text: current?['description']?.toString() ?? '',
    );
    final category = TextEditingController(
      text: current?['category']?.toString() ?? '',
    );
    final duration = TextEditingController(
      text: current?['durationHours']?.toString() ?? '1',
    );
    final dueDate = TextEditingController(
      text: current?['dueDate']?.toString() ?? '',
    );
    var mandatory = current?['mandatory'] == true;
    var active = current?['isActive'] != false;
    final key = GlobalKey<FormState>();
    final saved = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setState) => AlertDialog(
          title: Text(current == null ? 'Add course' : 'Edit course'),
          content: Form(
            key: key,
            child: SingleChildScrollView(
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  _required(title, 'Course title'),
                  TextField(
                    controller: description,
                    minLines: 2,
                    maxLines: 4,
                    decoration: const InputDecoration(labelText: 'Description'),
                  ),
                  TextField(
                    controller: category,
                    decoration: const InputDecoration(labelText: 'Category'),
                  ),
                  TextFormField(
                    controller: duration,
                    keyboardType: const TextInputType.numberWithOptions(
                      decimal: true,
                    ),
                    decoration: const InputDecoration(
                      labelText: 'Duration hours',
                    ),
                    validator: (value) => double.tryParse(value ?? '') == null
                        ? 'Enter a number'
                        : null,
                  ),
                  TextField(
                    controller: dueDate,
                    decoration: const InputDecoration(
                      labelText: 'Due date (optional) · YYYY-MM-DD',
                    ),
                  ),
                  SwitchListTile(
                    contentPadding: EdgeInsets.zero,
                    title: const Text('Mandatory training'),
                    value: mandatory,
                    onChanged: (value) => setState(() => mandatory = value),
                  ),
                  SwitchListTile(
                    contentPadding: EdgeInsets.zero,
                    title: const Text('Active'),
                    value: active,
                    onChanged: (value) => setState(() => active = value),
                  ),
                ],
              ),
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(dialogContext),
              child: const Text(Strings.cancel),
            ),
            FilledButton(
              onPressed: () {
                if (key.currentState?.validate() ?? false)
                  Navigator.pop(dialogContext, true);
              },
              child: const Text(Strings.save),
            ),
          ],
        ),
      ),
    );
    if (saved == true && context.mounted) {
      try {
        final data = {
          'title': title.text.trim(),
          'description': description.text.trim().isEmpty
              ? null
              : description.text.trim(),
          'category': category.text.trim().isEmpty
              ? null
              : category.text.trim(),
          'durationHours': double.tryParse(duration.text.trim()) ?? 0,
          'dueDate': dueDate.text.trim().isEmpty ? null : dueDate.text.trim(),
          'mandatory': mandatory,
          'isActive': active,
        };
        final api = ref.read(apiClientProvider);
        if (current == null) {
          await api.postMap('/admin/training/courses', data: data);
        } else {
          await api.putMap(
            '/admin/training/courses/${current['id']}',
            data: data,
          );
        }
        ref.invalidate(adminTrainingCoursesProvider);
        ref.invalidate(coursesProvider);
        if (context.mounted)
          ScaffoldMessenger.of(context).showSnackBar(
            const SnackBar(content: Text(Strings.savedSuccessfully)),
          );
      } catch (error) {
        if (context.mounted)
          ScaffoldMessenger.of(context).showSnackBar(
            SnackBar(content: Text('Course update failed: $error')),
          );
      }
    }
    for (final controller in [
      title,
      description,
      category,
      duration,
      dueDate,
    ]) {
      controller.dispose();
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final courses = ref.watch(adminTrainingCoursesProvider);
    return Column(
      children: [
        _add('Add course', () => _edit(context, ref)),
        Expanded(
          child: AsyncBody(
            value: courses,
            onRetry: () => ref.invalidate(adminTrainingCoursesProvider),
            isEmpty: (items) => items.isEmpty,
            emptyTitle: 'No training courses',
            emptyMessage: 'Add a mandatory or optional course.',
            builder: (items) => ListView.separated(
              padding: const EdgeInsets.all(12),
              itemCount: items.length,
              separatorBuilder: (_, _) => const SizedBox(height: 8),
              itemBuilder: (context, index) {
                final item = Map<String, dynamic>.from(items[index] as Map);
                return Card(
                  child: ListTile(
                    title: Text(item['title']?.toString() ?? 'Course'),
                    subtitle: Text(
                      '${item['mandatory'] == true ? 'Mandatory' : 'Optional'} · ${item['durationHours']}h · ${item['completed'] ?? 0}/${item['enrolled'] ?? 0} completed · ${item['isActive'] == true ? 'Active' : 'Inactive'}',
                    ),
                    trailing: const Icon(Icons.edit_outlined),
                    onTap: () => _edit(context, ref, current: item),
                  ),
                );
              },
            ),
          ),
        ),
      ],
    );
  }
}

Widget _add(String label, VoidCallback onPressed) => Padding(
  padding: const EdgeInsets.all(12),
  child: Align(
    alignment: Alignment.centerRight,
    child: FilledButton.icon(
      onPressed: onPressed,
      icon: const Icon(Icons.add),
      label: Text(label),
    ),
  ),
);

Widget _required(TextEditingController controller, String label) => Padding(
  padding: const EdgeInsets.only(bottom: 8),
  child: TextFormField(
    controller: controller,
    decoration: InputDecoration(labelText: label),
    validator: (value) =>
        value == null || value.trim().isEmpty ? Strings.fieldRequired : null,
  ),
);
