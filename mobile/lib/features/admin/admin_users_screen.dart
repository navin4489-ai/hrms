import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/strings.dart';
import '../../providers/admin_provider.dart';
import '../../providers/auth_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';
import '../../widgets/status_chip.dart';

/// User-access administration (FR-ADM-140..143).
class AdminUsersScreen extends ConsumerStatefulWidget {
  const AdminUsersScreen({super.key});

  @override
  ConsumerState<AdminUsersScreen> createState() => _AdminUsersScreenState();
}

class _AdminUsersScreenState extends ConsumerState<AdminUsersScreen> {
  final _search = TextEditingController();
  String? _status;
  String _query = '';

  @override
  void dispose() {
    _search.dispose();
    super.dispose();
  }

  void _refresh(AdminUsersQuery query) =>
      ref.invalidate(adminUsersProvider(query));

  Future<void> _perform(String message, Future<void> Function() action) async {
    try {
      await action();
      if (!mounted) return;
      ScaffoldMessenger.of(context)
          .showSnackBar(SnackBar(content: Text(message)));
      ref.invalidate(adminUsersProvider);
    } catch (error) {
      if (!mounted) return;
      ScaffoldMessenger.of(context)
          .showSnackBar(SnackBar(content: Text('Admin action failed: $error')));
    }
  }

  Future<void> _editRoles(AdminUserEntry user) async {
    final auth = ref.read(authStateProvider);
    if (!auth.hasPermission('admin.roles')) return;

    final availableRoles = await ref.read(adminRolesProvider.future);
    if (!mounted) return;

    final selected = user.roles.toSet();
    final result = await showDialog<List<String>>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) => AlertDialog(
          title: Text('Roles for ${user.displayName}'),
          content: SizedBox(
            width: 420,
            child: SingleChildScrollView(
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  for (final role in availableRoles)
                    CheckboxListTile(
                      contentPadding: EdgeInsets.zero,
                      value: selected.contains(role.name),
                      title: Text(role.name),
                      subtitle: Text(role.description),
                      onChanged:
                          user.id == auth.user?.id && role.name == 'SuperAdmin'
                          ? null
                          : (checked) => setDialogState(() {
                              if (checked == true) {
                                selected.add(role.name);
                              } else {
                                selected.remove(role.name);
                              }
                            }),
                    ),
                ],
              ),
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.of(dialogContext).pop(),
              child: const Text(Strings.cancel),
            ),
            FilledButton(
              onPressed: () =>
                  Navigator.of(dialogContext)
                      .pop(selected.toList(growable: false)),
              child: const Text(Strings.save),
            ),
          ],
        ),
      ),
    );

    if (result == null || !mounted) return;
    await _perform(
      'User roles updated',
      () => ref.read(adminUserActionsProvider).updateRoles(user.id, result),
    );
  }

  Future<void> _openActions(AdminUserEntry user) async {
    final currentUserId = ref.read(authStateProvider).user?.id;
    await showModalBottomSheet<void>(
      context: context,
      showDragHandle: true,
      builder: (sheetContext) => SafeArea(
        child: Wrap(
          children: [
            if (ref.read(authStateProvider).hasPermission('admin.roles'))
              ListTile(
                leading: const Icon(Icons.manage_accounts_outlined),
                title: const Text('Manage roles'),
                onTap: () {
                  Navigator.of(sheetContext).pop();
                  _editRoles(user);
                },
              ),
            if (user.id != currentUserId)
              ListTile(
                leading: Icon(
                  user.status == 'Active'
                      ? Icons.person_off_outlined
                      : Icons.person_outline_rounded,
                ),
                title: Text(
                  user.status == 'Active'
                      ? 'Suspend account'
                      : 'Activate account',
                ),
                onTap: () {
                  Navigator.of(sheetContext).pop();
                  final actions = ref.read(adminUserActionsProvider);
                  _perform(
                    user.status == 'Active'
                        ? 'User suspended'
                        : 'User activated',
                    () => user.status == 'Active'
                        ? actions.suspend(user.id)
                        : actions.activate(user.id),
                  );
                },
              ),
            ListTile(
              leading: const Icon(Icons.devices_outlined),
              title: const Text('Revoke all sessions'),
              onTap: () {
                Navigator.of(sheetContext).pop();
                _perform(
                  'All user sessions revoked',
                  () => ref
                      .read(adminUserActionsProvider)
                      .revokeSessions(user.id),
                );
              },
            ),
          ],
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final query = AdminUsersQuery(query: _query, status: _status);
    final users = ref.watch(adminUsersProvider(query));
    return AppScaffold(
      title: 'User accounts',
      body: Column(
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 8),
            child: Column(
              children: [
                TextField(
                  controller: _search,
                  textInputAction: TextInputAction.search,
                  onSubmitted: (value) => setState(() => _query = value.trim()),
                  decoration: InputDecoration(
                    labelText: Strings.search,
                    hintText: 'Name, employee number or email',
                    prefixIcon: const Icon(Icons.search_rounded),
                    suffixIcon: IconButton(
                      tooltip: Strings.search,
                      onPressed: () =>
                          setState(() => _query = _search.text.trim()),
                      icon: const Icon(Icons.arrow_forward_rounded),
                    ),
                  ),
                ),
                const SizedBox(height: 10),
                DropdownButtonFormField<String?>(
                  initialValue: _status,
                  decoration: const InputDecoration(
                    labelText: 'Account status',
                    isDense: true,
                  ),
                  items: const [
                    DropdownMenuItem<String?>(value: null, child: Text('All')),
                    DropdownMenuItem<String?>(
                      value: 'Active',
                      child: Text('Active'),
                    ),
                    DropdownMenuItem<String?>(
                      value: 'Suspended',
                      child: Text('Suspended'),
                    ),
                    DropdownMenuItem<String?>(
                      value: 'Terminated',
                      child: Text('Terminated'),
                    ),
                  ],
                  onChanged: (value) => setState(() => _status = value),
                ),
              ],
            ),
          ),
          Expanded(
            child: RefreshIndicator(
              onRefresh: () async => _refresh(query),
              child: AsyncBody(
                value: users,
                onRetry: () => _refresh(query),
                isEmpty: (page) => page.items.isEmpty,
                emptyTitle: 'No user accounts',
                emptyMessage: 'Create an employee login from Add employee.',
                emptyIcon: Icons.manage_accounts_outlined,
                builder: (page) => ListView.separated(
                  physics: const AlwaysScrollableScrollPhysics(),
                  padding: const EdgeInsets.fromLTRB(16, 4, 16, 20),
                  itemCount: page.items.length,
                  separatorBuilder: (_, __) => const SizedBox(height: 8),
                  itemBuilder: (context, index) {
                    final user = page.items[index];
                    return Card(
                      child: ListTile(
                        leading: CircleAvatar(
                          child: Text(
                            user.displayName.isEmpty
                                ? '?'
                                : user.displayName[0].toUpperCase(),
                          ),
                        ),
                        title: Text(user.displayName),
                        subtitle: Text(
                          [
                            if (user.employeeNumber.isNotEmpty)
                              user.employeeNumber,
                            if (user.workEmail != null) user.workEmail!,
                            if (user.roles.isNotEmpty) user.roles.join(', '),
                            'Sessions: ${user.sessionsActive}',
                          ].join(' · '),
                        ),
                        trailing: StatusChip(status: user.status, dense: true),
                        onTap: () => _openActions(user),
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
}
