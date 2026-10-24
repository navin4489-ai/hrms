import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/strings.dart';
import '../../providers/auth_provider.dart';
import '../../providers/directory_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';

/// Employee directory with search + department/location filters
/// (FR-EMP-024, privacy-aware: the server only returns safe fields).
class DirectoryScreen extends ConsumerStatefulWidget {
  const DirectoryScreen({super.key});

  @override
  ConsumerState<DirectoryScreen> createState() => _DirectoryScreenState();
}

class _DirectoryScreenState extends ConsumerState<DirectoryScreen> {
  final _searchController = TextEditingController();
  Timer? _debounce;
  String _search = '';
  String? _departmentId;
  String? _locationId;

  @override
  void dispose() {
    _debounce?.cancel();
    _searchController.dispose();
    super.dispose();
  }

  void _onSearchChanged(String value) {
    _debounce?.cancel();
    _debounce = Timer(const Duration(milliseconds: 350), () {
      if (mounted) setState(() => _search = value);
    });
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final query = DirectoryQuery(
      q: _search,
      departmentId: _departmentId,
      locationId: _locationId,
    );
    final page = ref.watch(directoryProvider(query));
    final canCreateEmployee = ref
        .watch(authStateProvider)
        .hasPermission('employee.create');
    final departments = ref.watch(departmentsProvider).value ?? const [];
    final locations = ref.watch(locationsProvider).value ?? const [];

    return AppScaffold(
      title: Strings.directory,
      floatingActionButton: canCreateEmployee
          ? FloatingActionButton.extended(
              onPressed: () async {
                final created = await context.push<bool>('/more/directory/new');
                if (!context.mounted || created != true) return;
                ref.invalidate(directoryProvider(query));
                ScaffoldMessenger.of(context).showSnackBar(
                  const SnackBar(content: Text(Strings.employeeCreated)),
                );
              },
              icon: const Icon(Icons.person_add_alt_1_rounded),
              label: const Text(Strings.addEmployee),
            )
          : null,
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(directoryProvider(query)),
        child: Column(
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 12, 16, 8),
              child: Column(
                children: [
                  TextField(
                    controller: _searchController,
                    onChanged: _onSearchChanged,
                    textInputAction: TextInputAction.search,
                    decoration: InputDecoration(
                      labelText: Strings.search,
                      hintText: 'Name, employee number or email',
                      prefixIcon: const Icon(Icons.search_rounded),
                      suffixIcon: _search.isEmpty
                          ? null
                          : IconButton(
                              tooltip: Strings.close,
                              onPressed: () {
                                _searchController.clear();
                                setState(() => _search = '');
                              },
                              icon: const Icon(Icons.close_rounded),
                            ),
                    ),
                  ),
                  const SizedBox(height: 10),
                  Row(
                    children: [
                      Expanded(
                        child: DropdownButtonFormField<String?>(
                          initialValue: _departmentId,
                          decoration: const InputDecoration(
                            labelText: Strings.department,
                            isDense: true,
                          ),
                          items: [
                            const DropdownMenuItem(
                              value: null,
                              child: Text(Strings.all),
                            ),
                            for (final unit in departments)
                              DropdownMenuItem(
                                value: unit.id,
                                child: Text(
                                  unit.name,
                                  maxLines: 1,
                                  overflow: TextOverflow.ellipsis,
                                ),
                              ),
                          ],
                          onChanged: (value) =>
                              setState(() => _departmentId = value),
                        ),
                      ),
                      const SizedBox(width: 10),
                      Expanded(
                        child: DropdownButtonFormField<String?>(
                          initialValue: _locationId,
                          decoration: const InputDecoration(
                            labelText: Strings.location,
                            isDense: true,
                          ),
                          items: [
                            const DropdownMenuItem(
                              value: null,
                              child: Text(Strings.all),
                            ),
                            for (final location in locations)
                              DropdownMenuItem(
                                value: location.id,
                                child: Text(
                                  location.name,
                                  maxLines: 1,
                                  overflow: TextOverflow.ellipsis,
                                ),
                              ),
                          ],
                          onChanged: (value) =>
                              setState(() => _locationId = value),
                        ),
                      ),
                    ],
                  ),
                ],
              ),
            ),
            Expanded(
              child: AsyncBody(
                value: page,
                onRetry: () => ref.invalidate(directoryProvider(query)),
                isEmpty: (data) => data.items.isEmpty,
                emptyTitle: Strings.noEmployees,
                emptyMessage: Strings.noResultsHint,
                emptyIcon: Icons.badge_outlined,
                builder: (data) => ListView.separated(
                  physics: const AlwaysScrollableScrollPhysics(),
                  padding: const EdgeInsets.fromLTRB(16, 4, 16, 16),
                  itemCount: data.items.length,
                  separatorBuilder: (_, __) => const SizedBox(height: 8),
                  itemBuilder: (context, index) {
                    final entry = data.items[index];
                    return Card(
                      clipBehavior: Clip.antiAlias,
                      child: ListTile(
                        leading: CircleAvatar(
                          backgroundColor: theme.colorScheme.primaryContainer,
                          foregroundColor: theme.colorScheme.onPrimaryContainer,
                          child: Text(
                            entry.initials,
                            style: const TextStyle(
                              fontWeight: FontWeight.w700,
                              fontSize: 13,
                            ),
                          ),
                        ),
                        title: Text(entry.displayName),
                        subtitle: Text(
                          [
                            if (entry.designation != null) entry.designation!,
                            if (entry.department != null) entry.department!,
                            if (entry.location != null) entry.location!,
                          ].join(' · '),
                          maxLines: 2,
                          overflow: TextOverflow.ellipsis,
                        ),
                        trailing: const Icon(Icons.chevron_right_rounded),
                        onTap: () =>
                            context.go('/more/directory/${entry.employeeId}'),
                      ),
                    );
                  },
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
