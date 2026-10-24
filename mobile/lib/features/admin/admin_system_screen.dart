import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/strings.dart';
import '../../providers/admin_provider.dart';
import '../../providers/auth_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';

class AdminSystemScreen extends StatelessWidget {
  const AdminSystemScreen({super.key});

  @override
  Widget build(BuildContext context) => Consumer(
    builder: (context, ref, _) {
      final auth = ref.watch(authStateProvider);
      final tabs = <Tab>[];
      final pages = <Widget>[];
      if (auth.hasPermission('admin.roles')) {
        tabs.add(const Tab(text: 'Roles'));
        pages.add(const _AdminRolesTab());
      }
      if (auth.hasPermission('admin.config')) {
        tabs.add(const Tab(text: 'Configuration'));
        pages.add(const _AdminConfigurationTab());
        tabs.add(const Tab(text: 'Operations'));
        pages.add(const _AdminOperationsTab());
      }
      if (auth.hasPermission('audit.view')) {
        tabs.add(const Tab(text: 'Audit'));
        pages.add(const _AdminAuditTab());
      }
      if (tabs.isEmpty) {
        return const AppScaffold(
          title: 'System administration',
          body: Center(
            child: Text('You do not have system-admin permissions.'),
          ),
        );
      }
      return DefaultTabController(
        length: tabs.length,
        child: AppScaffold(
          title: 'System administration',
          bottom: TabBar(isScrollable: true, tabs: tabs),
          body: TabBarView(children: pages),
        ),
      );
    },
  );
}

class _AdminRolesTab extends ConsumerWidget {
  const _AdminRolesTab();

  Future<void> _editRole(
    BuildContext context,
    WidgetRef ref,
    AdminRoleEntry role,
  ) async {
    if (role.name == 'SuperAdmin') {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('The SuperAdmin role is locked.')),
      );
      return;
    }
    final controller = TextEditingController(text: role.permissions.join(', '));
    final permissions = await showDialog<List<String>>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: Text('Permissions · ${role.name}'),
        content: TextField(
          controller: controller,
          minLines: 4,
          maxLines: 10,
          decoration: const InputDecoration(
            labelText: 'Permission codes',
            hintText: 'employee.view, leave.approve',
            helperText: 'Separate codes with commas or new lines.',
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(dialogContext).pop(),
            child: const Text(Strings.cancel),
          ),
          FilledButton(
            onPressed: () => Navigator.of(dialogContext).pop(
              controller.text
                  .split(RegExp(r'[,\n]'))
                  .map((value) => value.trim())
                  .where((value) => value.isNotEmpty)
                  .toSet()
                  .toList(growable: false),
            ),
            child: const Text(Strings.save),
          ),
        ],
      ),
    );
    controller.dispose();
    if (permissions == null || !context.mounted) return;
    try {
      await ref
          .read(adminUserActionsProvider)
          .updateRolePermissions(role.name, permissions);
      ref.invalidate(adminRolesProvider);
      if (context.mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(const SnackBar(content: Text('Role permissions saved')));
      }
    } catch (error) {
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Could not update role: $error')),
        );
      }
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final roles = ref.watch(adminRolesProvider);
    return AsyncBody(
      value: roles,
      onRetry: () => ref.invalidate(adminRolesProvider),
      isEmpty: (data) => data.isEmpty,
      emptyTitle: 'No roles configured',
      emptyMessage: 'Role definitions are not available.',
      emptyIcon: Icons.admin_panel_settings_outlined,
      builder: (data) => ListView.separated(
        padding: const EdgeInsets.all(16),
        itemCount: data.length,
        separatorBuilder: (_, __) => const SizedBox(height: 8),
        itemBuilder: (context, index) {
          final role = data[index];
          return Card(
            child: ListTile(
              leading: const Icon(Icons.shield_outlined),
              title: Text(role.name),
              subtitle: Text(
                '${role.description}\n${role.permissions.length} permissions · ${role.userCount} users',
              ),
              isThreeLine: true,
              trailing: role.name == 'SuperAdmin'
                  ? const Icon(Icons.lock_outline_rounded)
                  : const Icon(Icons.chevron_right_rounded),
              onTap: () => _editRole(context, ref, role),
            ),
          );
        },
      ),
    );
  }
}

class _AdminConfigurationTab extends ConsumerWidget {
  const _AdminConfigurationTab();

  Future<void> _editValue(
    BuildContext context,
    WidgetRef ref,
    AdminConfigurationEntry entry,
  ) async {
    final controller = TextEditingController(text: entry.value);
    final value = await showDialog<String>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: Text(entry.key),
        content: TextField(
          controller: controller,
          minLines: entry.type == 'Json' ? 4 : 1,
          maxLines: entry.type == 'Json' ? 10 : 4,
          decoration: InputDecoration(
            labelText: '${entry.type} value',
            helperText: entry.description,
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(dialogContext).pop(),
            child: const Text(Strings.cancel),
          ),
          FilledButton(
            onPressed: () => Navigator.of(dialogContext).pop(controller.text),
            child: const Text(Strings.save),
          ),
        ],
      ),
    );
    controller.dispose();
    if (value == null || !context.mounted) return;
    try {
      await ref
          .read(adminUserActionsProvider)
          .updateConfiguration(entry.key, value);
      ref.invalidate(adminConfigurationProvider);
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text(Strings.savedSuccessfully)),
        );
      }
    } catch (error) {
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Configuration update failed: $error')),
        );
      }
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final configuration = ref.watch(adminConfigurationProvider);
    return AsyncBody(
      value: configuration,
      onRetry: () => ref.invalidate(adminConfigurationProvider),
      isEmpty: (data) => data.isEmpty,
      emptyTitle: 'No configuration values',
      emptyMessage: 'System configuration is not available.',
      emptyIcon: Icons.tune_rounded,
      builder: (data) => ListView.separated(
        padding: const EdgeInsets.all(16),
        itemCount: data.length,
        separatorBuilder: (_, __) => const SizedBox(height: 8),
        itemBuilder: (context, index) {
          final item = data[index];
          return Card(
            child: ListTile(
              title: Text(item.key),
              subtitle: Text('${item.value}\n${item.description}'),
              isThreeLine: true,
              trailing: const Icon(Icons.edit_outlined),
              onTap: () => _editValue(context, ref, item),
            ),
          );
        },
      ),
    );
  }
}

class _AdminAuditTab extends ConsumerWidget {
  const _AdminAuditTab();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final audit = ref.watch(adminAuditProvider);
    return AsyncBody(
      value: audit,
      onRetry: () => ref.invalidate(adminAuditProvider),
      isEmpty: (data) => data.items.isEmpty,
      emptyTitle: 'No audit events',
      emptyMessage: 'Administrative activity will appear here.',
      emptyIcon: Icons.history_rounded,
      builder: (data) => ListView.separated(
        padding: const EdgeInsets.all(16),
        itemCount: data.items.length,
        separatorBuilder: (_, __) => const SizedBox(height: 8),
        itemBuilder: (context, index) {
          final event = data.items[index];
          return Card(
            child: ListTile(
              leading: Icon(
                Icons.history_rounded,
                color: Theme.of(context).colorScheme.primary,
              ),
              title: Text(event.action),
              subtitle: Text(
                [
                  event.actorName,
                  if (event.actorEmployeeNumber != null)
                    event.actorEmployeeNumber!,
                  if (event.entityType != null) event.entityType!,
                  event.timestamp,
                ].join(' · '),
              ),
              trailing: Text(event.outcome),
            ),
          );
        },
      ),
    );
  }
}

class _AdminOperationsTab extends ConsumerWidget {
  const _AdminOperationsTab();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final operations = ref.watch(adminOperationsProvider);
    return AsyncBody(
      value: operations,
      onRetry: () => ref.invalidate(adminOperationsProvider),
      isEmpty: (data) => data.isEmpty,
      emptyTitle: 'Operations unavailable',
      emptyMessage: 'The API did not return an operations snapshot.',
      emptyIcon: Icons.monitor_heart_outlined,
      builder: (data) {
        final jobs = data['backgroundJobs'];
        final jobList = jobs is List
            ? jobs.whereType<Map>().toList()
            : const [];
        return ListView(
          padding: const EdgeInsets.all(16),
          children: [
            Wrap(
              spacing: 10,
              runSpacing: 10,
              children: [
                _MetricCard(
                  label: 'Uptime',
                  value: '${data['uptimeMinutes'] ?? 0} min',
                  icon: Icons.timer_outlined,
                ),
                _MetricCard(
                  label: 'Notification failures · 24h',
                  value: '${data['notificationFailures24h'] ?? 0}',
                  icon: Icons.notifications_off_outlined,
                ),
                _MetricCard(
                  label: 'Integration errors · 24h',
                  value: '${data['integrationErrors24h'] ?? 0}',
                  icon: Icons.sync_problem_outlined,
                ),
              ],
            ),
            const SizedBox(height: 18),
            Text(
              'Background jobs',
              style: Theme.of(context).textTheme.titleMedium,
            ),
            const SizedBox(height: 8),
            for (final raw in jobList)
              Card(
                child: ListTile(
                  leading: const Icon(Icons.work_history_outlined),
                  title: Text('${raw['name'] ?? 'Job'}'),
                  subtitle: Text('Last run: ${raw['lastRunAt'] ?? 'Not run'}'),
                  trailing: Text('${raw['status'] ?? 'Unknown'}'),
                ),
              ),
          ],
        );
      },
    );
  }
}

class _MetricCard extends StatelessWidget {
  const _MetricCard({
    required this.label,
    required this.value,
    required this.icon,
  });

  final String label;
  final String value;
  final IconData icon;

  @override
  Widget build(BuildContext context) => SizedBox(
    width: 220,
    child: Card(
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Icon(icon, color: Theme.of(context).colorScheme.primary),
            const SizedBox(height: 8),
            Text(value, style: Theme.of(context).textTheme.titleLarge),
            Text(label, style: Theme.of(context).textTheme.bodySmall),
          ],
        ),
      ),
    ),
  );
}
