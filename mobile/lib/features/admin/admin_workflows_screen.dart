import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/strings.dart';
import '../../providers/admin_provider.dart';
import '../../providers/api_provider.dart';
import '../../providers/auth_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';

final adminWorkflowsProvider = FutureProvider<List<dynamic>>(
  (ref) => ref.watch(apiClientProvider).getList('/workflows'),
);

class _WorkflowStepDraft {
  _WorkflowStepDraft(Map<String, dynamic>? json)
    : name = TextEditingController(text: json?['name']?.toString() ?? ''),
      approverValue = json?['approverValue']?.toString(),
      approverType = json?['approverType']?.toString() ?? 'Manager',
      slaDays = TextEditingController(text: json?['slaDays']?.toString() ?? '');

  final TextEditingController name;
  String? approverValue;
  final TextEditingController slaDays;
  String approverType;

  void dispose() {
    name.dispose();
    slaDays.dispose();
  }
}

class AdminWorkflowsScreen extends ConsumerWidget {
  const AdminWorkflowsScreen({super.key});

  Future<void> _publishVersion(
    BuildContext context,
    WidgetRef ref,
    Map<String, dynamic> workflow,
  ) async {
    final auth = ref.read(authStateProvider);
    final roles = auth.hasPermission('admin.roles')
        ? await ref.read(adminRolesProvider.future)
        : const <AdminRoleEntry>[];
    final users = auth.hasPermission('admin.users')
        ? await ref.read(adminUsersProvider(const AdminUsersQuery()).future)
        : null;
    if (!context.mounted) return;
    final steps = <_WorkflowStepDraft>[
      for (final raw
          in (workflow['steps'] as List? ?? const []).whereType<Map>())
        _WorkflowStepDraft(Map<String, dynamic>.from(raw)),
    ];
    final name = TextEditingController(
      text: workflow['name']?.toString() ?? '',
    );
    final formKey = GlobalKey<FormState>();
    final published = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) => AlertDialog(
          title: Text('New version · ${workflow['code']}'),
          content: SizedBox(
            width: 560,
            child: Form(
              key: formKey,
              child: SingleChildScrollView(
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    TextFormField(
                      controller: name,
                      decoration: const InputDecoration(
                        labelText: 'Workflow name',
                      ),
                      validator: _required,
                    ),
                    const SizedBox(height: 12),
                    for (var index = 0; index < steps.length; index++)
                      Card(
                        child: Padding(
                          padding: const EdgeInsets.all(10),
                          child: Column(
                            children: [
                              Row(
                                children: [
                                  Expanded(child: Text('Step ${index + 1}')),
                                  IconButton(
                                    tooltip: 'Remove step',
                                    onPressed: steps.length < 2
                                        ? null
                                        : () => setDialogState(() {
                                            steps.removeAt(index).dispose();
                                          }),
                                    icon: const Icon(
                                      Icons.delete_outline_rounded,
                                    ),
                                  ),
                                ],
                              ),
                              TextFormField(
                                controller: steps[index].name,
                                decoration: const InputDecoration(
                                  labelText: 'Step name',
                                ),
                                validator: _required,
                              ),
                              const SizedBox(height: 8),
                              DropdownButtonFormField<String>(
                                initialValue: steps[index].approverType,
                                decoration: const InputDecoration(
                                  labelText: 'Approver rule',
                                ),
                                items: const [
                                  DropdownMenuItem(
                                    value: 'Manager',
                                    child: Text('Reporting manager'),
                                  ),
                                  DropdownMenuItem(
                                    value: 'Role',
                                    child: Text('Role'),
                                  ),
                                  DropdownMenuItem(
                                    value: 'NamedUser',
                                    child: Text('Named user ID'),
                                  ),
                                  DropdownMenuItem(
                                    value: 'SelfAndManager',
                                    child: Text('Manager with role fallback'),
                                  ),
                                ],
                                onChanged: (value) => setDialogState(() {
                                  steps[index].approverType =
                                      value ?? 'Manager';
                                }),
                              ),
                              if (steps[index].approverType == 'Role' ||
                                  steps[index].approverType ==
                                      'SelfAndManager') ...[
                                const SizedBox(height: 8),
                                if (roles.isEmpty)
                                  TextFormField(
                                    initialValue: steps[index].approverValue,
                                    decoration: InputDecoration(
                                      labelText:
                                          steps[index].approverType ==
                                              'SelfAndManager'
                                          ? 'Fallback role name'
                                          : 'Approver role name',
                                    ),
                                    validator: _required,
                                    onChanged: (value) =>
                                        steps[index].approverValue = value
                                            .trim(),
                                  )
                                else
                                  DropdownButtonFormField<String?>(
                                    initialValue: steps[index].approverValue,
                                    decoration: InputDecoration(
                                      labelText:
                                          steps[index].approverType ==
                                              'SelfAndManager'
                                          ? 'Fallback role'
                                          : 'Approver role',
                                    ),
                                    items: [
                                      for (final role in roles)
                                        DropdownMenuItem<String?>(
                                          value: role.name,
                                          child: Text(role.name),
                                        ),
                                    ],
                                    validator: (value) => value == null
                                        ? Strings.fieldRequired
                                        : null,
                                    onChanged: (value) => setDialogState(
                                      () => steps[index].approverValue = value,
                                    ),
                                  ),
                              ],
                              if (steps[index].approverType == 'NamedUser') ...[
                                const SizedBox(height: 8),
                                if (users == null)
                                  TextFormField(
                                    initialValue: steps[index].approverValue,
                                    decoration: const InputDecoration(
                                      labelText: 'Active user account ID',
                                    ),
                                    validator: _required,
                                    onChanged: (value) =>
                                        steps[index].approverValue = value
                                            .trim(),
                                  )
                                else
                                  DropdownButtonFormField<String?>(
                                    initialValue: steps[index].approverValue,
                                    decoration: const InputDecoration(
                                      labelText: 'Named approver',
                                    ),
                                    items: [
                                      for (final user in users.items)
                                        DropdownMenuItem<String?>(
                                          value: user.id,
                                          child: Text(
                                            '${user.displayName} · ${user.employeeNumber}',
                                          ),
                                        ),
                                    ],
                                    validator: (value) => value == null
                                        ? Strings.fieldRequired
                                        : null,
                                    onChanged: (value) => setDialogState(
                                      () => steps[index].approverValue = value,
                                    ),
                                  ),
                              ],
                              TextFormField(
                                controller: steps[index].slaDays,
                                keyboardType: TextInputType.number,
                                decoration: const InputDecoration(
                                  labelText: 'SLA days (optional)',
                                ),
                                validator: (value) {
                                  if (value == null || value.trim().isEmpty)
                                    return null;
                                  final days = int.tryParse(value.trim());
                                  return days == null || days < 1 || days > 365
                                      ? 'Enter 1–365 days'
                                      : null;
                                },
                              ),
                            ],
                          ),
                        ),
                      ),
                    OutlinedButton.icon(
                      onPressed: steps.length >= 10
                          ? null
                          : () => setDialogState(
                              () => steps.add(_WorkflowStepDraft(null)),
                            ),
                      icon: const Icon(Icons.add),
                      label: const Text('Add approval step'),
                    ),
                    const SizedBox(height: 8),
                    const Text(
                      'Publishing creates an immutable version. In-flight requests stay on the version they started with.',
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
                if (formKey.currentState?.validate() ?? false) {
                  Navigator.pop(dialogContext, true);
                }
              },
              child: const Text('Publish version'),
            ),
          ],
        ),
      ),
    );
    if (published == true && context.mounted) {
      try {
        await ref
            .read(apiClientProvider)
            .postMap(
              '/workflows/${workflow['id']}/versions',
              data: {
                'name': name.text.trim(),
                'steps': [
                  for (final step in steps)
                    {
                      'name': step.name.text.trim(),
                      'approverType': step.approverType,
                      'approverValue': step.approverValue,
                      'slaDays': int.tryParse(step.slaDays.text.trim()),
                    },
                ],
              },
            );
        ref.invalidate(adminWorkflowsProvider);
        if (context.mounted) {
          ScaffoldMessenger.of(context).showSnackBar(
            const SnackBar(content: Text('Workflow version published')),
          );
        }
      } catch (error) {
        if (context.mounted) {
          ScaffoldMessenger.of(context).showSnackBar(
            SnackBar(
              content: Text('Workflow version was not published: $error'),
            ),
          );
        }
      }
    }
    name.dispose();
    for (final step in steps) {
      step.dispose();
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final workflows = ref.watch(adminWorkflowsProvider);
    return AppScaffold(
      title: 'Approval workflows',
      body: AsyncBody(
        value: workflows,
        onRetry: () => ref.invalidate(adminWorkflowsProvider),
        isEmpty: (items) => items.isEmpty,
        emptyTitle: 'No workflows configured',
        emptyMessage: 'The API has no workflow definitions to display.',
        emptyIcon: Icons.alt_route_rounded,
        builder: (items) => ListView.separated(
          padding: const EdgeInsets.all(16),
          itemCount: items.length,
          separatorBuilder: (_, _) => const SizedBox(height: 8),
          itemBuilder: (context, index) {
            final item = Map<String, dynamic>.from(items[index] as Map);
            final steps = item['steps'] is List
                ? (item['steps'] as List).whereType<Map>().toList()
                : <Map>[];
            return Card(
              child: ExpansionTile(
                leading: Icon(
                  item['isActive'] == true
                      ? Icons.check_circle_outline_rounded
                      : Icons.pause_circle_outline_rounded,
                ),
                title: Text(
                  item['name']?.toString() ??
                      item['code']?.toString() ??
                      'Workflow',
                ),
                subtitle: Text(
                  '${item['code'] ?? ''} · v${item['version'] ?? 1} · ${item['isActive'] == true ? 'Active' : 'Inactive'}',
                ),
                children: [
                  for (final step in steps)
                    ListTile(
                      dense: true,
                      leading: CircleAvatar(
                        radius: 14,
                        child: Text('${step['stepNumber'] ?? ''}'),
                      ),
                      title: Text(step['name']?.toString() ?? 'Approval step'),
                      subtitle: Text(
                        '${step['approverType'] ?? ''}'
                        '${step['approverValue'] == null ? '' : ' · ${step['approverValue']}'}'
                        '${step['slaDays'] == null ? '' : ' · SLA ${step['slaDays']} days'}',
                      ),
                    ),
                  Padding(
                    padding: const EdgeInsets.fromLTRB(16, 0, 16, 12),
                    child: Align(
                      alignment: Alignment.centerRight,
                      child: OutlinedButton.icon(
                        onPressed: () => _publishVersion(context, ref, item),
                        icon: const Icon(Icons.edit_outlined),
                        label: const Text('Create new version'),
                      ),
                    ),
                  ),
                ],
              ),
            );
          },
        ),
      ),
    );
  }
}

String? _required(String? value) =>
    value == null || value.trim().isEmpty ? Strings.fieldRequired : null;
