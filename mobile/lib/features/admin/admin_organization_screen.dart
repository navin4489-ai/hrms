import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/strings.dart';
import '../../models/directory_models.dart';
import '../../providers/api_provider.dart';
import '../../providers/directory_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';
import '../../widgets/status_chip.dart';

class AdminOrganizationScreen extends StatelessWidget {
  const AdminOrganizationScreen({super.key});

  @override
  Widget build(BuildContext context) => DefaultTabController(
    length: 4,
    child: AppScaffold(
      title: 'Organization setup',
      bottom: const TabBar(
        isScrollable: true,
        tabs: [
          Tab(text: 'Organization units'),
          Tab(text: 'Locations'),
          Tab(text: 'Holidays'),
          Tab(text: 'Designations & grades'),
        ],
      ),
      body: const TabBarView(
        children: [
          _OrgUnitsTab(),
          _LocationsTab(),
          _HolidayCalendarTab(),
          _DesignationsTab(),
        ],
      ),
    ),
  );
}

class _OrgUnitsTab extends ConsumerStatefulWidget {
  const _OrgUnitsTab();

  @override
  ConsumerState<_OrgUnitsTab> createState() => _OrgUnitsTabState();
}

class _OrgUnitsTabState extends ConsumerState<_OrgUnitsTab> {
  bool _busy = false;

  Future<void> _edit({OrgUnit? current}) async {
    final options = await ref.read(employeeCreateOptionsProvider.future);
    if (!mounted) return;
    final code = TextEditingController(text: current?.code ?? '');
    final name = TextEditingController(text: current?.name ?? '');
    final from = TextEditingController(text: current?.effectiveFrom ?? '');
    final to = TextEditingController(text: current?.effectiveTo ?? '');
    var type = current?.type ?? 'Department';
    var parentId = current?.parentId;
    var managerId = current?.managerId;
    var status = current?.status ?? 'Active';
    final formKey = GlobalKey<FormState>();
    final saved = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) => AlertDialog(
          title: Text(
            current == null ? 'Add organization unit' : 'Edit ${current.name}',
          ),
          content: SizedBox(
            width: 480,
            child: Form(
              key: formKey,
              child: SingleChildScrollView(
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    DropdownButtonFormField<String>(
                      initialValue: type,
                      decoration: const InputDecoration(labelText: 'Unit type'),
                      items: const [
                        DropdownMenuItem(
                          value: 'Entity',
                          child: Text('Legal entity'),
                        ),
                        DropdownMenuItem(
                          value: 'BusinessUnit',
                          child: Text('Business unit'),
                        ),
                        DropdownMenuItem(
                          value: 'Department',
                          child: Text('Department'),
                        ),
                        DropdownMenuItem(value: 'Team', child: Text('Team')),
                      ],
                      onChanged: (value) =>
                          setDialogState(() => type = value ?? type),
                    ),
                    const SizedBox(height: 10),
                    TextFormField(
                      controller: code,
                      maxLength: 50,
                      decoration: const InputDecoration(labelText: 'Code'),
                      validator: _required,
                    ),
                    const SizedBox(height: 10),
                    TextFormField(
                      controller: name,
                      maxLength: 200,
                      decoration: const InputDecoration(labelText: 'Name'),
                      validator: _required,
                    ),
                    const SizedBox(height: 10),
                    DropdownButtonFormField<String?>(
                      initialValue: parentId,
                      decoration: const InputDecoration(
                        labelText: 'Parent unit',
                      ),
                      items: [
                        const DropdownMenuItem<String?>(
                          value: null,
                          child: Text('None'),
                        ),
                        for (final unit in options.orgUnits.where(
                          (u) => u.id != current?.id,
                        ))
                          DropdownMenuItem<String?>(
                            value: unit.id,
                            child: Text(
                              '${unit.name} · ${unit.type ?? 'Unit'}',
                            ),
                          ),
                      ],
                      onChanged: (value) =>
                          setDialogState(() => parentId = value),
                    ),
                    const SizedBox(height: 10),
                    DropdownButtonFormField<String?>(
                      initialValue: managerId,
                      decoration: const InputDecoration(
                        labelText: 'Reporting manager',
                      ),
                      items: [
                        const DropdownMenuItem<String?>(
                          value: null,
                          child: Text('None'),
                        ),
                        for (final manager in options.managers)
                          DropdownMenuItem<String?>(
                            value: manager.employeeId,
                            child: Text(manager.label),
                          ),
                      ],
                      onChanged: (value) =>
                          setDialogState(() => managerId = value),
                    ),
                    const SizedBox(height: 10),
                    Row(
                      children: [
                        Expanded(
                          child: TextFormField(
                            controller: from,
                            decoration: const InputDecoration(
                              labelText: 'Effective from',
                              hintText: 'YYYY-MM-DD',
                            ),
                          ),
                        ),
                        const SizedBox(width: 10),
                        Expanded(
                          child: TextFormField(
                            controller: to,
                            decoration: const InputDecoration(
                              labelText: 'Effective to',
                              hintText: 'YYYY-MM-DD',
                            ),
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 10),
                    _statusDropdown(
                      status,
                      (value) => setDialogState(() => status = value),
                    ),
                  ],
                ),
              ),
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.of(dialogContext).pop(false),
              child: const Text(Strings.cancel),
            ),
            FilledButton(
              onPressed: () {
                if (formKey.currentState?.validate() ?? false) {
                  Navigator.of(dialogContext).pop(true);
                }
              },
              child: const Text(Strings.save),
            ),
          ],
        ),
      ),
    );
    if (saved != true || !mounted) {
      _dispose([code, name, from, to]);
      return;
    }
    await _submit(
      path: current == null ? '/org-units' : '/org-units/${current.id}',
      data: {
        'code': code.text.trim(),
        'name': name.text.trim(),
        'type': type,
        'parentId': parentId,
        'managerId': managerId,
        'effectiveFrom': _dateOrNull(from.text),
        'effectiveTo': _dateOrNull(to.text),
        'status': status,
      },
      update: current != null,
      controllers: [code, name, from, to],
    );
  }

  Future<void> _submit({
    required String path,
    required Map<String, dynamic> data,
    required bool update,
    required List<TextEditingController> controllers,
  }) async {
    setState(() => _busy = true);
    try {
      final api = ref.read(apiClientProvider);
      if (update) {
        await api.putMap(path, data: data);
      } else {
        await api.postMap(path, data: data);
      }
      ref.invalidate(departmentsProvider);
      ref.invalidate(locationsProvider);
      ref.invalidate(employeeCreateOptionsProvider);
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text(Strings.savedSuccessfully)),
        );
      }
    } catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Organization update failed: $error')),
        );
      }
    } finally {
      _dispose(controllers);
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final allUnits = ref.watch(employeeCreateOptionsProvider);
    return Column(
      children: [
        Padding(
          padding: const EdgeInsets.all(12),
          child: Align(
            alignment: Alignment.centerRight,
            child: FilledButton.icon(
              onPressed: _busy ? null : () => _edit(),
              icon: const Icon(Icons.add),
              label: const Text('Add unit'),
            ),
          ),
        ),
        Expanded(
          child: AsyncBody(
            value: allUnits,
            onRetry: () => ref.invalidate(employeeCreateOptionsProvider),
            isEmpty: (value) => value.orgUnits.isEmpty,
            emptyTitle: 'No organization units',
            emptyMessage: 'Add a legal entity or organization unit.',
            builder: (value) => ListView.separated(
              padding: const EdgeInsets.fromLTRB(12, 0, 12, 20),
              itemCount: value.orgUnits.length,
              separatorBuilder: (_, _) => const SizedBox(height: 8),
              itemBuilder: (context, index) {
                final unit = value.orgUnits[index];
                return Card(
                  child: ListTile(
                    leading: const Icon(Icons.account_tree_outlined),
                    title: Text(unit.name),
                    subtitle: Text(
                      [
                        unit.code ?? '',
                        unit.type ?? '',
                        if (unit.managerName != null)
                          'Manager: ${unit.managerName}',
                      ].where((part) => part.isNotEmpty).join(' · '),
                    ),
                    trailing: Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        StatusChip(
                          status: unit.status ?? 'Active',
                          dense: true,
                        ),
                        IconButton(
                          tooltip: 'Edit organization unit',
                          onPressed: _busy ? null : () => _edit(current: unit),
                          icon: const Icon(Icons.edit_outlined),
                        ),
                      ],
                    ),
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

class _LocationsTab extends ConsumerStatefulWidget {
  const _LocationsTab();

  @override
  ConsumerState<_LocationsTab> createState() => _LocationsTabState();
}

class _LocationsTabState extends ConsumerState<_LocationsTab> {
  Future<void> _edit({LocationEntry? current}) async {
    final code = TextEditingController(text: current?.code ?? '');
    final name = TextEditingController(text: current?.name ?? '');
    final city = TextEditingController(text: current?.city ?? '');
    final country = TextEditingController(text: current?.country ?? 'IN');
    final timeZone = TextEditingController(
      text: current?.timeZone ?? 'Asia/Kolkata',
    );
    var status = current?.status ?? 'Active';
    final formKey = GlobalKey<FormState>();
    final saved = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) => AlertDialog(
          title: Text(
            current == null ? 'Add location' : 'Edit ${current.name}',
          ),
          content: Form(
            key: formKey,
            child: SingleChildScrollView(
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  for (final field in [
                    (code, 'Code'),
                    (name, 'Name'),
                    (city, 'City'),
                    (country, 'Country code'),
                    (timeZone, 'Time zone'),
                  ]) ...[
                    TextFormField(
                      controller: field.$1,
                      decoration: InputDecoration(labelText: field.$2),
                      validator: _required,
                    ),
                    const SizedBox(height: 8),
                  ],
                  _statusDropdown(
                    status,
                    (value) => setDialogState(() => status = value),
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
                if (formKey.currentState?.validate() ?? false)
                  Navigator.pop(dialogContext, true);
              },
              child: const Text(Strings.save),
            ),
          ],
        ),
      ),
    );
    final controllers = [code, name, city, country, timeZone];
    if (saved != true || !mounted) {
      _dispose(controllers);
      return;
    }
    await _saveLocation(current, {
      'code': code.text.trim(),
      'name': name.text.trim(),
      'city': city.text.trim(),
      'country': country.text.trim().toUpperCase(),
      'timeZone': timeZone.text.trim(),
      'status': status,
    }, controllers);
  }

  Future<void> _saveLocation(
    LocationEntry? current,
    Map<String, dynamic> data,
    List<TextEditingController> controllers,
  ) async {
    try {
      final api = ref.read(apiClientProvider);
      if (current == null) {
        await api.postMap('/locations', data: data);
      } else {
        await api.putMap('/locations/${current.id}', data: data);
      }
      ref.invalidate(locationsProvider);
      ref.invalidate(employeeCreateOptionsProvider);
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text(Strings.savedSuccessfully)),
        );
      }
    } catch (error) {
      if (mounted)
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Location update failed: $error')),
        );
    } finally {
      _dispose(controllers);
    }
  }

  @override
  Widget build(BuildContext context) {
    final locations = ref.watch(locationsProvider);
    return Column(
      children: [
        Padding(
          padding: const EdgeInsets.all(12),
          child: Align(
            alignment: Alignment.centerRight,
            child: FilledButton.icon(
              onPressed: () => _edit(),
              icon: const Icon(Icons.add),
              label: const Text('Add location'),
            ),
          ),
        ),
        Expanded(
          child: AsyncBody(
            value: locations,
            onRetry: () => ref.invalidate(locationsProvider),
            isEmpty: (items) => items.isEmpty,
            emptyTitle: 'No locations',
            emptyMessage: 'Add a work location.',
            builder: (items) => ListView.separated(
              padding: const EdgeInsets.fromLTRB(12, 0, 12, 20),
              itemCount: items.length,
              separatorBuilder: (_, _) => const SizedBox(height: 8),
              itemBuilder: (context, index) {
                final item = items[index];
                return Card(
                  child: ListTile(
                    title: Text(item.name),
                    subtitle: Text(
                      '${item.code ?? ''} · ${item.city ?? ''} · ${item.timeZone ?? ''}',
                    ),
                    trailing: IconButton(
                      onPressed: () => _edit(current: item),
                      icon: const Icon(Icons.edit_outlined),
                    ),
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

class _DesignationsTab extends ConsumerStatefulWidget {
  const _DesignationsTab();

  @override
  ConsumerState<_DesignationsTab> createState() => _DesignationsTabState();
}

final _adminHolidayListProvider =
    FutureProvider.family<List<dynamic>, (int, String?)>(
      (ref, query) => ref
          .watch(apiClientProvider)
          .getList(
            '/holidays',
            query: {
              'year': query.$1,
              if (query.$2 != null) 'locationId': query.$2,
            },
          ),
    );

class _HolidayCalendarTab extends ConsumerStatefulWidget {
  const _HolidayCalendarTab();

  @override
  ConsumerState<_HolidayCalendarTab> createState() =>
      _HolidayCalendarTabState();
}

class _HolidayCalendarTabState extends ConsumerState<_HolidayCalendarTab> {
  final int _year = DateTime.now().year;
  String? _locationId;

  Future<void> _edit({Map<String, dynamic>? current}) async {
    final locations = await ref.read(locationsProvider.future);
    if (!mounted) return;
    final name = TextEditingController(
      text: current?['name']?.toString() ?? '',
    );
    var date =
        DateTime.tryParse(current?['date']?.toString() ?? '') ??
        DateTime(_year, 1, 1);
    var type = current?['type']?.toString() ?? 'Public';
    var locationId = current?['locationId']?.toString();
    final formKey = GlobalKey<FormState>();
    final saved = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) => AlertDialog(
          title: Text(current == null ? 'Add holiday' : 'Edit holiday'),
          content: Form(
            key: formKey,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                ListTile(
                  contentPadding: EdgeInsets.zero,
                  leading: const Icon(Icons.calendar_month_outlined),
                  title: Text(
                    '${date.year}-${date.month.toString().padLeft(2, '0')}-${date.day.toString().padLeft(2, '0')}',
                  ),
                  trailing: const Icon(Icons.edit_calendar_outlined),
                  onTap: () async {
                    final picked = await showDatePicker(
                      context: dialogContext,
                      initialDate: date,
                      firstDate: DateTime(_year - 2),
                      lastDate: DateTime(_year + 5, 12, 31),
                    );
                    if (picked != null) setDialogState(() => date = picked);
                  },
                ),
                TextFormField(
                  controller: name,
                  decoration: const InputDecoration(labelText: 'Holiday name'),
                  validator: _required,
                ),
                const SizedBox(height: 10),
                DropdownButtonFormField<String>(
                  initialValue: type,
                  decoration: const InputDecoration(labelText: 'Holiday type'),
                  items: const [
                    DropdownMenuItem(
                      value: 'Public',
                      child: Text('Public holiday'),
                    ),
                    DropdownMenuItem(
                      value: 'Optional',
                      child: Text('Optional holiday'),
                    ),
                    DropdownMenuItem(
                      value: 'Shutdown',
                      child: Text('Company shutdown'),
                    ),
                  ],
                  onChanged: (value) =>
                      setDialogState(() => type = value ?? type),
                ),
                const SizedBox(height: 10),
                DropdownButtonFormField<String?>(
                  initialValue: locationId,
                  decoration: const InputDecoration(
                    labelText: 'Location scope',
                  ),
                  items: [
                    const DropdownMenuItem<String?>(
                      value: null,
                      child: Text('All locations'),
                    ),
                    for (final item in locations)
                      DropdownMenuItem<String?>(
                        value: item.id,
                        child: Text(item.name),
                      ),
                  ],
                  onChanged: (value) =>
                      setDialogState(() => locationId = value),
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
              onPressed: () {
                if (formKey.currentState?.validate() ?? false)
                  Navigator.pop(dialogContext, true);
              },
              child: const Text(Strings.save),
            ),
          ],
        ),
      ),
    );
    if (saved != true || !mounted) {
      name.dispose();
      return;
    }
    try {
      final path = current == null ? '/holidays' : '/holidays/${current['id']}';
      final data = {
        'date':
            '${date.year.toString().padLeft(4, '0')}-${date.month.toString().padLeft(2, '0')}-${date.day.toString().padLeft(2, '0')}',
        'name': name.text.trim(),
        'type': type,
        'locationId': locationId,
      };
      final api = ref.read(apiClientProvider);
      if (current == null) {
        await api.postMap(path, data: data);
      } else {
        await api.putMap(path, data: data);
      }
      ref.invalidate(_adminHolidayListProvider);
      if (mounted)
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text(Strings.savedSuccessfully)),
        );
    } catch (error) {
      if (mounted)
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Holiday update failed: $error')),
        );
    } finally {
      name.dispose();
    }
  }

  @override
  Widget build(BuildContext context) {
    final locations =
        ref.watch(locationsProvider).value ?? const <LocationEntry>[];
    final holidays = ref.watch(_adminHolidayListProvider((_year, _locationId)));
    return Column(
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(12, 10, 12, 4),
          child: Row(
            children: [
              Expanded(
                child: DropdownButtonFormField<String?>(
                  initialValue: _locationId,
                  decoration: const InputDecoration(
                    labelText: 'Location',
                    isDense: true,
                  ),
                  items: [
                    const DropdownMenuItem<String?>(
                      value: null,
                      child: Text('All locations'),
                    ),
                    for (final location in locations)
                      DropdownMenuItem<String?>(
                        value: location.id,
                        child: Text(location.name),
                      ),
                  ],
                  onChanged: (value) => setState(() => _locationId = value),
                ),
              ),
              const SizedBox(width: 8),
              IconButton.filledTonal(
                onPressed: () => _edit(),
                icon: const Icon(Icons.add),
                tooltip: 'Add holiday',
              ),
            ],
          ),
        ),
        Expanded(
          child: AsyncBody(
            value: holidays,
            onRetry: () =>
                ref.invalidate(_adminHolidayListProvider((_year, _locationId))),
            isEmpty: (items) => items.isEmpty,
            emptyTitle: 'No holidays for $_year',
            emptyMessage: 'Add a holiday to this calendar.',
            emptyIcon: Icons.event_available_outlined,
            builder: (items) => ListView.separated(
              padding: const EdgeInsets.fromLTRB(12, 4, 12, 20),
              itemCount: items.length,
              separatorBuilder: (_, _) => const SizedBox(height: 8),
              itemBuilder: (context, index) {
                final item = Map<String, dynamic>.from(items[index] as Map);
                return Card(
                  child: ListTile(
                    leading: const Icon(Icons.event_outlined),
                    title: Text(item['name']?.toString() ?? 'Holiday'),
                    subtitle: Text(
                      '${item['date'] ?? ''} · ${item['type'] ?? ''}',
                    ),
                    trailing: IconButton(
                      onPressed: () => _edit(current: item),
                      icon: const Icon(Icons.edit_outlined),
                    ),
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

class _DesignationsTabState extends ConsumerState<_DesignationsTab> {
  Future<void> _edit({DesignationEntry? current}) async {
    final code = TextEditingController(text: current?.code ?? '');
    final name = TextEditingController(text: current?.name ?? '');
    final grade = TextEditingController(text: current?.grade ?? '');
    var status = current?.status ?? 'Active';
    final formKey = GlobalKey<FormState>();
    final saved = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) => AlertDialog(
          title: Text(
            current == null ? 'Add designation' : 'Edit ${current.name}',
          ),
          content: Form(
            key: formKey,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                TextFormField(
                  controller: code,
                  decoration: const InputDecoration(labelText: 'Code'),
                  validator: _required,
                ),
                TextFormField(
                  controller: name,
                  decoration: const InputDecoration(labelText: 'Designation'),
                  validator: _required,
                ),
                TextFormField(
                  controller: grade,
                  decoration: const InputDecoration(labelText: 'Grade / level'),
                ),
                const SizedBox(height: 10),
                _statusDropdown(
                  status,
                  (value) => setDialogState(() => status = value),
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
              onPressed: () {
                if (formKey.currentState?.validate() ?? false)
                  Navigator.pop(dialogContext, true);
              },
              child: const Text(Strings.save),
            ),
          ],
        ),
      ),
    );
    final controllers = [code, name, grade];
    if (saved != true || !mounted) {
      _dispose(controllers);
      return;
    }
    try {
      final api = ref.read(apiClientProvider);
      final data = {
        'code': code.text.trim(),
        'name': name.text.trim(),
        'grade': grade.text.trim().isEmpty ? null : grade.text.trim(),
        'status': status,
      };
      if (current == null) {
        await api.postMap('/designations', data: data);
      } else {
        await api.putMap('/designations/${current.id}', data: data);
      }
      ref.invalidate(employeeCreateOptionsProvider);
      if (mounted)
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text(Strings.savedSuccessfully)),
        );
    } catch (error) {
      if (mounted)
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Designation update failed: $error')),
        );
    } finally {
      _dispose(controllers);
    }
  }

  @override
  Widget build(BuildContext context) {
    final entries = ref.watch(employeeCreateOptionsProvider);
    return Column(
      children: [
        Padding(
          padding: const EdgeInsets.all(12),
          child: Align(
            alignment: Alignment.centerRight,
            child: FilledButton.icon(
              onPressed: () => _edit(),
              icon: const Icon(Icons.add),
              label: const Text('Add designation'),
            ),
          ),
        ),
        Expanded(
          child: AsyncBody(
            value: entries,
            onRetry: () => ref.invalidate(employeeCreateOptionsProvider),
            isEmpty: (value) => value.designations.isEmpty,
            emptyTitle: 'No designations',
            emptyMessage: 'Add a designation and optional grade.',
            builder: (value) => ListView.separated(
              padding: const EdgeInsets.fromLTRB(12, 0, 12, 20),
              itemCount: value.designations.length,
              separatorBuilder: (_, _) => const SizedBox(height: 8),
              itemBuilder: (context, index) {
                final item = value.designations[index];
                return Card(
                  child: ListTile(
                    title: Text(item.name),
                    subtitle: Text(
                      '${item.code ?? ''}${item.grade == null ? '' : ' · Grade ${item.grade}'}',
                    ),
                    trailing: IconButton(
                      onPressed: () => _edit(current: item),
                      icon: const Icon(Icons.edit_outlined),
                    ),
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

Widget _statusDropdown(String value, ValueChanged<String> onChanged) =>
    DropdownButtonFormField<String>(
      initialValue: value,
      decoration: const InputDecoration(labelText: 'Status'),
      items: const [
        DropdownMenuItem(value: 'Active', child: Text('Active')),
        DropdownMenuItem(value: 'Inactive', child: Text('Inactive')),
      ],
      onChanged: (next) {
        if (next != null) onChanged(next);
      },
    );

String? _required(String? value) =>
    value == null || value.trim().isEmpty ? Strings.fieldRequired : null;

String? _dateOrNull(String value) {
  final trimmed = value.trim();
  return trimmed.isEmpty ? null : trimmed;
}

void _dispose(List<TextEditingController> controllers) {
  for (final controller in controllers) {
    controller.dispose();
  }
}
