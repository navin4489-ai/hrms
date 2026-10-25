import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/network/api_exception.dart';
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
    var saving = false;
    String? formError;
    String? weekdaysError;
    Map<String, String> fieldErrors = {};
    final saved = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) {
          Future<void> save() async {
            if (!(formKey.currentState?.validate() ?? false)) return;
            if (weekdays.isEmpty) {
              setDialogState(() {
                weekdaysError = 'Select at least one working day.';
              });
              return;
            }
            setDialogState(() {
              saving = true;
              formError = null;
              weekdaysError = null;
              fieldErrors = {};
            });
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
              if (dialogContext.mounted) Navigator.pop(dialogContext, true);
            } on ApiException catch (error) {
              setDialogState(() {
                fieldErrors = _shiftFieldErrors(error);
                if (error.statusCode == 409 &&
                    error.message.toLowerCase().contains('code')) {
                  fieldErrors['code'] = error.message;
                }
                formError = error.errors.isEmpty ? error.message : error.detail;
                saving = false;
              });
            } catch (error) {
              setDialogState(() {
                formError = 'Could not save shift: $error';
                saving = false;
              });
            }
          }

          return AlertDialog(
            title: Text(current == null ? 'Add work shift' : 'Edit work shift'),
            content: SizedBox(
              width: 500,
              child: Form(
                key: formKey,
                child: SingleChildScrollView(
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      if (formError != null) _shiftFormError(formError!),
                      _requiredField(
                        code,
                        'Shift code',
                        _shiftFieldError(fieldErrors, 'code'),
                      ),
                      _requiredField(
                        name,
                        'Shift name',
                        _shiftFieldError(fieldErrors, 'name'),
                      ),
                      Row(
                        children: [
                          Expanded(
                            child: _timeField(
                              start,
                              'Start time',
                              _shiftFieldError(fieldErrors, 'startTime'),
                            ),
                          ),
                          const SizedBox(width: 10),
                          Expanded(
                            child: _timeField(
                              end,
                              'End time',
                              _shiftFieldError(fieldErrors, 'endTime'),
                            ),
                          ),
                        ],
                      ),
                      Row(
                        children: [
                          Expanded(
                            child: _integerField(
                              grace,
                              'Grace minutes',
                              0,
                              180,
                              _shiftFieldError(fieldErrors, 'graceMinutes'),
                            ),
                          ),
                          const SizedBox(width: 10),
                          Expanded(
                            child: _integerField(
                              breakMinutes,
                              'Break minutes',
                              0,
                              600,
                              _shiftFieldError(fieldErrors, 'breakMinutes'),
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
                              onSelected: saving
                                  ? null
                                  : (selected) => setDialogState(() {
                                      if (selected) {
                                        weekdays.add(day);
                                      } else {
                                        weekdays.remove(day);
                                      }
                                      weekdaysError = null;
                                    }),
                            ),
                        ],
                      ),
                      if (weekdaysError != null ||
                          _shiftFieldError(fieldErrors, 'weekDays') != null)
                        Align(
                          alignment: Alignment.centerLeft,
                          child: Padding(
                            padding: const EdgeInsets.only(top: 4),
                            child: Text(
                              weekdaysError ??
                                  _shiftFieldError(fieldErrors, 'weekDays')!,
                              style: TextStyle(
                                color: Theme.of(dialogContext)
                                    .colorScheme
                                    .error,
                              ),
                            ),
                          ),
                        ),
                      SwitchListTile(
                        contentPadding: EdgeInsets.zero,
                        title: const Text('Default shift'),
                        value: isDefault,
                        onChanged: saving
                            ? null
                            : (value) =>
                                  setDialogState(() => isDefault = value),
                      ),
                      SwitchListTile(
                        contentPadding: EdgeInsets.zero,
                        title: const Text('Active'),
                        value: isActive,
                        onChanged: saving
                            ? null
                            : (value) => setDialogState(() => isActive = value),
                      ),
                    ],
                  ),
                ),
              ),
            ),
            actions: [
              TextButton(
                onPressed: saving ? null : () => Navigator.pop(dialogContext),
                child: const Text(Strings.cancel),
              ),
              FilledButton(
                onPressed: saving ? null : save,
                child: saving
                    ? const SizedBox(
                        width: 18,
                        height: 18,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : const Text(Strings.save),
              ),
            ],
          );
        },
      ),
    );
    if (saved == true && context.mounted) {
      ref.invalidate(adminShiftsProvider);
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(const SnackBar(content: Text(Strings.savedSuccessfully)));
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

Map<String, String> _shiftFieldErrors(ApiException error) => {
  for (final entry in error.errors.entries)
    if (entry.value.isNotEmpty) entry.key: entry.value.first,
};

String? _shiftFieldError(Map<String, String> errors, String field) {
  for (final entry in errors.entries) {
    if (entry.key.toLowerCase() == field.toLowerCase()) return entry.value;
  }
  return null;
}

Widget _shiftFormError(String message) => Padding(
  padding: const EdgeInsets.only(bottom: 10),
  child: Align(
    alignment: Alignment.centerLeft,
    child: Text(message, style: const TextStyle(color: Colors.red)),
  ),
);

Widget _requiredField(
  TextEditingController controller,
  String label,
  String? errorText,
) => Padding(
  padding: const EdgeInsets.only(bottom: 8),
  child: TextFormField(
    controller: controller,
    decoration: InputDecoration(labelText: label, errorText: errorText),
    validator: (value) =>
        value == null || value.trim().isEmpty ? Strings.fieldRequired : null,
  ),
);

Widget _timeField(
  TextEditingController controller,
  String label,
  String? errorText,
) => Padding(
  padding: const EdgeInsets.only(bottom: 8),
  child: TextFormField(
    controller: controller,
    keyboardType: TextInputType.datetime,
    decoration: InputDecoration(
      labelText: label,
      hintText: 'HH:mm',
      errorText: errorText,
    ),
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
  String? errorText,
) => Padding(
  padding: const EdgeInsets.only(bottom: 8),
  child: TextFormField(
    controller: controller,
    keyboardType: TextInputType.number,
    decoration: InputDecoration(labelText: label, errorText: errorText),
    validator: (value) {
      final parsed = int.tryParse(value ?? '');
      return parsed == null || parsed < min || parsed > max
          ? 'Enter $min–$max'
          : null;
    },
  ),
);
