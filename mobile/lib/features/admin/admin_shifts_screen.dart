import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/strings.dart';
import '../../providers/api_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';
import '../../widgets/status_chip.dart';

final adminShiftsProvider = FutureProvider<List<dynamic>>(
  (ref) => ref.watch(apiClientProvider).getList('/admin/shifts'),
);

class AdminShiftsScreen extends ConsumerWidget {
  const AdminShiftsScreen({super.key});

  Future<void> _edit(
    BuildContext context,
    WidgetRef ref, {
    Map<String, dynamic>? current,
  }) async {
    final code = TextEditingController(
      text: current?['code']?.toString() ?? '',
    );
    final name = TextEditingController(
      text: current?['name']?.toString() ?? '',
    );
    final start = TextEditingController(
      text: current?['startTime']?.toString() ?? '09:00',
    );
    final end = TextEditingController(
      text: current?['endTime']?.toString() ?? '18:00',
    );
    final grace = TextEditingController(
      text: current?['graceMinutes']?.toString() ?? '10',
    );
    final breakMinutes = TextEditingController(
      text: current?['breakMinutes']?.toString() ?? '60',
    );
    final weekdays = <String>{
      ...((current?['weekDays'] as List?)?.map((item) => item.toString()) ??
          const ['Mon', 'Tue', 'Wed', 'Thu', 'Fri']),
    };
    var isDefault = current?['isDefault'] == true;
    var isActive = current?['isActive'] != false;
    final formKey = GlobalKey<FormState>();
    final saved = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setState) => AlertDialog(
          title: Text(current == null ? 'Add work shift' : 'Edit work shift'),
          content: SizedBox(
            width: 500,
            child: Form(
              key: formKey,
              child: SingleChildScrollView(
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    _requiredField(code, 'Shift code'),
                    _requiredField(name, 'Shift name'),
                    Row(
                      children: [
                        Expanded(child: _timeField(start, 'Start time')),
                        const SizedBox(width: 10),
                        Expanded(child: _timeField(end, 'End time')),
                      ],
                    ),
                    Row(
                      children: [
                        Expanded(
                          child: _integerField(grace, 'Grace minutes', 0, 180),
                        ),
                        const SizedBox(width: 10),
                        Expanded(
                          child: _integerField(
                            breakMinutes,
                            'Break minutes',
                            0,
                            600,
                          ),
                        ),
                      ],
                    ),
                    const Align(
                      alignment: Alignment.centerLeft,
                      child: Padding(
                        padding: EdgeInsets.only(top: 12),
                        child: Text('Working days'),
                      ),
                    ),
                    Wrap(
                      spacing: 2,
                      children: [
                        for (final day in const [
                          'Mon',
                          'Tue',
                          'Wed',
                          'Thu',
                          'Fri',
                          'Sat',
                          'Sun',
                        ])
                          FilterChip(
                            label: Text(day),
                            selected: weekdays.contains(day),
                            onSelected: (selected) => setState(() {
                              if (selected) {
                                weekdays.add(day);
                              } else {
                                weekdays.remove(day);
                              }
                            }),
                          ),
                      ],
                    ),
                    SwitchListTile(
                      contentPadding: EdgeInsets.zero,
                      title: const Text('Default shift'),
                      value: isDefault,
                      onChanged: (value) => setState(() => isDefault = value),
                    ),
                    SwitchListTile(
                      contentPadding: EdgeInsets.zero,
                      title: const Text('Active'),
                      value: isActive,
                      onChanged: (value) => setState(() => isActive = value),
                    ),
                  ],
                ),
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
                if (weekdays.isEmpty) return;
                if (formKey.currentState?.validate() ?? false)
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
          'code': code.text.trim(),
          'name': name.text.trim(),
          'startTime': start.text.trim(),
          'endTime': end.text.trim(),
          'graceMinutes': int.parse(grace.text.trim()),
          'breakMinutes': int.parse(breakMinutes.text.trim()),
          'weekDays': [
            'Mon',
            'Tue',
            'Wed',
            'Thu',
            'Fri',
            'Sat',
            'Sun',
          ].where(weekdays.contains).toList(),
          'isDefault': isDefault,
          'isActive': isActive,
        };
        final api = ref.read(apiClientProvider);
        if (current == null) {
          await api.postMap('/admin/shifts', data: data);
        } else {
          await api.putMap('/admin/shifts/${current['id']}', data: data);
        }
        ref.invalidate(adminShiftsProvider);
        if (context.mounted)
          ScaffoldMessenger.of(context).showSnackBar(
            const SnackBar(content: Text(Strings.savedSuccessfully)),
          );
      } catch (error) {
        if (context.mounted)
          ScaffoldMessenger.of(context).showSnackBar(
            SnackBar(content: Text('Shift update failed: $error')),
          );
      }
    }
    for (final controller in [code, name, start, end, grace, breakMinutes]) {
      controller.dispose();
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final shifts = ref.watch(adminShiftsProvider);
    return AppScaffold(
      title: 'Shift configuration',
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => _edit(context, ref),
        icon: const Icon(Icons.add),
        label: const Text('Add shift'),
      ),
      body: AsyncBody(
        value: shifts,
        onRetry: () => ref.invalidate(adminShiftsProvider),
        isEmpty: (items) => items.isEmpty,
        emptyTitle: 'No shifts configured',
        emptyMessage:
            'Create a shift definition for the organization calendar.',
        emptyIcon: Icons.schedule_outlined,
        builder: (items) => ListView.separated(
          padding: const EdgeInsets.fromLTRB(16, 16, 16, 90),
          itemCount: items.length,
          separatorBuilder: (_, _) => const SizedBox(height: 8),
          itemBuilder: (context, index) {
            final item = Map<String, dynamic>.from(items[index] as Map);
            final days = (item['weekDays'] as List? ?? const []).join(', ');
            return Card(
              child: ListTile(
                leading: Icon(
                  item['isDefault'] == true
                      ? Icons.star_rounded
                      : Icons.schedule_rounded,
                ),
                title: Text('${item['name']} · ${item['code']}'),
                subtitle: Text(
                  '${item['startTime']}–${item['endTime']} · Grace ${item['graceMinutes']}m · Break ${item['breakMinutes']}m\n$days',
                ),
                isThreeLine: true,
                trailing: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    StatusChip(
                      status: item['isActive'] == true ? 'Active' : 'Inactive',
                      dense: true,
                    ),
                    IconButton(
                      onPressed: () => _edit(context, ref, current: item),
                      icon: const Icon(Icons.edit_outlined),
                    ),
                  ],
                ),
              ),
            );
          },
        ),
      ),
    );
  }
}

Widget _requiredField(TextEditingController controller, String label) =>
    Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: TextFormField(
        controller: controller,
        decoration: InputDecoration(labelText: label),
        validator: (value) => value == null || value.trim().isEmpty
            ? Strings.fieldRequired
            : null,
      ),
    );

Widget _timeField(TextEditingController controller, String label) => Padding(
  padding: const EdgeInsets.only(bottom: 8),
  child: TextFormField(
    controller: controller,
    keyboardType: TextInputType.datetime,
    decoration: InputDecoration(labelText: label, hintText: 'HH:mm'),
    validator: (value) =>
        value != null &&
            RegExp(r'^([01]\d|2[0-3]):[0-5]\d$').hasMatch(value.trim())
        ? null
        : 'Use HH:mm',
  ),
);

Widget _integerField(
  TextEditingController controller,
  String label,
  int min,
  int max,
) => Padding(
  padding: const EdgeInsets.only(bottom: 8),
  child: TextFormField(
    controller: controller,
    keyboardType: TextInputType.number,
    decoration: InputDecoration(labelText: label),
    validator: (value) {
      final parsed = int.tryParse(value ?? '');
      return parsed == null || parsed < min || parsed > max
          ? 'Enter $min–$max'
          : null;
    },
  ),
);
