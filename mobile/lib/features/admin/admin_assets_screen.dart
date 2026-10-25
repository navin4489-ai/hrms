import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/strings.dart';
import '../../providers/api_provider.dart';
import '../../providers/directory_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';
import '../../widgets/status_chip.dart';

final adminAssetInventoryProvider = FutureProvider<Map<String, dynamic>>((
  ref,
) async {
  return ref
      .watch(apiClientProvider)
      .getMap('/assets/inventory', query: {'page': 1, 'pageSize': 100});
});

class AdminAssetsScreen extends ConsumerStatefulWidget {
  const AdminAssetsScreen({super.key});

  @override
  ConsumerState<AdminAssetsScreen> createState() => _AdminAssetsScreenState();
}

class _AdminAssetsScreenState extends ConsumerState<AdminAssetsScreen> {
  static const _types = [
    'Laptop',
    'Phone',
    'Badge',
    'Sim',
    'Accessory',
    'Other',
  ];
  bool _busy = false;

  Future<void> _addAsset() async {
    final locations = await ref.read(locationsProvider.future);
    if (!mounted) return;
    final name = TextEditingController();
    final assetTag = TextEditingController();
    final serial = TextEditingController();
    final model = TextEditingController();
    final purchaseDate = TextEditingController();
    final warrantyExpiry = TextEditingController();
    String? locationId;
    var type = _types.first;
    final formKey = GlobalKey<FormState>();
    final submit = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) => AlertDialog(
          title: const Text('Add asset to inventory'),
          content: Form(
            key: formKey,
            child: SingleChildScrollView(
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  DropdownButtonFormField<String>(
                    initialValue: type,
                    decoration: const InputDecoration(labelText: 'Asset type'),
                    items: [
                      for (final value in _types)
                        DropdownMenuItem(value: value, child: Text(value)),
                    ],
                    onChanged: (value) =>
                        setDialogState(() => type = value ?? type),
                  ),
                  const SizedBox(height: 10),
                  TextFormField(
                    controller: name,
                    decoration: const InputDecoration(labelText: 'Name'),
                    validator: (value) => value == null || value.trim().isEmpty
                        ? Strings.fieldRequired
                        : null,
                  ),
                  const SizedBox(height: 10),
                  TextFormField(
                    controller: assetTag,
                    decoration: const InputDecoration(
                      labelText: 'Asset tag (optional)',
                    ),
                  ),
                  const SizedBox(height: 10),
                  TextFormField(
                    controller: serial,
                    decoration: const InputDecoration(
                      labelText: 'Serial number (optional)',
                    ),
                  ),
                  const SizedBox(height: 10),
                  TextFormField(
                    controller: model,
                    decoration: const InputDecoration(
                      labelText: 'Model (optional)',
                    ),
                  ),
                  const SizedBox(height: 10),
                  TextFormField(
                    controller: purchaseDate,
                    decoration: const InputDecoration(
                      labelText: 'Purchase date (optional)',
                      hintText: 'YYYY-MM-DD',
                    ),
                  ),
                  const SizedBox(height: 10),
                  TextFormField(
                    controller: warrantyExpiry,
                    decoration: const InputDecoration(
                      labelText: 'Warranty expiry (optional)',
                      hintText: 'YYYY-MM-DD',
                    ),
                  ),
                  const SizedBox(height: 10),
                  DropdownButtonFormField<String?>(
                    initialValue: locationId,
                    decoration: const InputDecoration(labelText: 'Location'),
                    items: [
                      const DropdownMenuItem<String?>(
                        value: null,
                        child: Text('Unassigned'),
                      ),
                      for (final location in locations)
                        DropdownMenuItem<String?>(
                          value: location.id,
                          child: Text(location.name),
                        ),
                    ],
                    onChanged: (value) =>
                        setDialogState(() => locationId = value),
                  ),
                ],
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
              child: const Text('Add asset'),
            ),
          ],
        ),
      ),
    );
    if (submit != true || !mounted) return;
    setState(() => _busy = true);
    try {
      await ref
          .read(apiClientProvider)
          .postMap(
            '/assets',
            data: {
              'assetType': type,
              'name': name.text.trim(),
              if (assetTag.text.trim().isNotEmpty)
                'assetTag': assetTag.text.trim(),
              if (serial.text.trim().isNotEmpty)
                'serialNumber': serial.text.trim(),
              if (model.text.trim().isNotEmpty) 'model': model.text.trim(),
              if (purchaseDate.text.trim().isNotEmpty)
                'purchaseDate': purchaseDate.text.trim(),
              if (warrantyExpiry.text.trim().isNotEmpty)
                'warrantyExpiry': warrantyExpiry.text.trim(),
              if (locationId != null) 'locationId': locationId,
            },
          );
      ref.invalidate(adminAssetInventoryProvider);
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Asset added to inventory')),
        );
      }
    } catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text('Could not add asset: $error')));
      }
    } finally {
      name.dispose();
      assetTag.dispose();
      serial.dispose();
      model.dispose();
      purchaseDate.dispose();
      warrantyExpiry.dispose();
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _assignAsset(String assetId) async {
    final employees = await ref.read(employeeCreateOptionsProvider.future);
    if (!mounted) return;
    final condition = TextEditingController(text: 'Good');
    final accessories = TextEditingController();
    String? employeeId;
    final selectedEmployeeId = await showDialog<String>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) => AlertDialog(
          title: const Text('Assign asset'),
          content: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              DropdownButtonFormField<String>(
                initialValue: employeeId,
                decoration: const InputDecoration(labelText: 'Employee'),
                items: [
                  for (final employee in employees.managers)
                    DropdownMenuItem(
                      value: employee.employeeId,
                      child: Text(employee.label),
                    ),
                ],
                onChanged: (value) => setDialogState(() => employeeId = value),
              ),
              const SizedBox(height: 10),
              TextField(
                controller: condition,
                decoration: const InputDecoration(labelText: 'Condition'),
              ),
              const SizedBox(height: 10),
              TextField(
                controller: accessories,
                decoration: const InputDecoration(
                  labelText: 'Accessories (optional)',
                ),
              ),
            ],
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.of(dialogContext).pop(),
              child: const Text(Strings.cancel),
            ),
            FilledButton(
              onPressed: employeeId == null
                  ? null
                  : () => Navigator.of(dialogContext).pop(employeeId),
              child: const Text('Assign'),
            ),
          ],
        ),
      ),
    );
    if (selectedEmployeeId == null || !mounted) {
      condition.dispose();
      accessories.dispose();
      return;
    }
    setState(() => _busy = true);
    try {
      await ref
          .read(apiClientProvider)
          .postMap(
            '/assets/$assetId/assign',
            data: {
              'employeeId': selectedEmployeeId,
              'condition': condition.text.trim().isEmpty
                  ? 'Good'
                  : condition.text.trim(),
              if (accessories.text.trim().isNotEmpty)
                'accessories': accessories.text.trim(),
            },
          );
      ref.invalidate(adminAssetInventoryProvider);
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(const SnackBar(content: Text('Asset assigned')));
      }
    } catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Could not assign asset: $error')),
        );
      }
    } finally {
      condition.dispose();
      accessories.dispose();
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _manageAsset(Map<String, dynamic> asset) async {
    final status = asset['status']?.toString() ?? 'Unknown';
    final options = await ref.read(employeeCreateOptionsProvider.future);
    if (!mounted) return;
    final allowedActions = switch (status) {
      'Assigned' || 'ReturnRequested' => const [
        ('ReturnToInventory', 'Return to inventory'),
        ('SendToRepair', 'Send to repair'),
        ('Transfer', 'Transfer to another employee'),
        ('MarkLost', 'Mark lost'),
        ('MarkDamaged', 'Mark damaged'),
        ('Dispose', 'Dispose asset'),
      ],
      'UnderRepair' => const [
        ('RepairComplete', 'Mark repair complete'),
        ('MarkLost', 'Mark lost'),
        ('Dispose', 'Dispose asset'),
      ],
      'Damaged' || 'Lost' => const [('Dispose', 'Dispose asset')],
      _ => const <(String, String)>[],
    };
    if (allowedActions.isEmpty) return;
    var action = allowedActions.first.$1;
    String? employeeId;
    final reason = TextEditingController();
    final condition = TextEditingController(
      text:
          asset['returnCondition']?.toString() ??
          asset['condition']?.toString() ??
          '',
    );
    final saved = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) => AlertDialog(
          title: Text('Manage ${asset['assetTag'] ?? 'asset'}'),
          content: SizedBox(
            width: 460,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                DropdownButtonFormField<String>(
                  initialValue: action,
                  decoration: const InputDecoration(labelText: 'Action'),
                  items: [
                    for (final item in allowedActions)
                      DropdownMenuItem(value: item.$1, child: Text(item.$2)),
                  ],
                  onChanged: (value) =>
                      setDialogState(() => action = value ?? action),
                ),
                if (action == 'Transfer') ...[
                  const SizedBox(height: 10),
                  DropdownButtonFormField<String?>(
                    initialValue: employeeId,
                    decoration: const InputDecoration(
                      labelText: 'New custodian',
                    ),
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
                ],
                const SizedBox(height: 10),
                TextField(
                  controller: condition,
                  onChanged: (_) => setDialogState(() {}),
                  decoration: const InputDecoration(
                    labelText: 'Condition (optional)',
                  ),
                ),
                const SizedBox(height: 10),
                TextField(
                  controller: reason,
                  onChanged: (_) => setDialogState(() {}),
                  minLines: 2,
                  maxLines: 4,
                  decoration: const InputDecoration(labelText: 'Reason'),
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
              onPressed:
                  reason.text.trim().isEmpty ||
                      (action == 'Transfer' && employeeId == null)
                  ? null
                  : () => Navigator.pop(dialogContext, true),
              child: const Text('Save action'),
            ),
          ],
        ),
      ),
    );
    if (saved != true || !mounted) {
      reason.dispose();
      condition.dispose();
      return;
    }
    setState(() => _busy = true);
    try {
      await ref
          .read(apiClientProvider)
          .postMap(
            '/assets/${asset['id']}/lifecycle',
            data: {
              'action': action,
              'reason': reason.text.trim(),
              if (condition.text.trim().isNotEmpty)
                'condition': condition.text.trim(),
              if (employeeId != null) 'employeeId': employeeId,
            },
          );
      ref.invalidate(adminAssetInventoryProvider);
      if (mounted)
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Asset lifecycle updated')),
        );
    } catch (error) {
      if (mounted)
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text('Asset action failed: $error')));
    } finally {
      reason.dispose();
      condition.dispose();
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final inventory = ref.watch(adminAssetInventoryProvider);
    return AppScaffold(
      title: 'Asset inventory',
      floatingActionButton: FloatingActionButton.extended(
        onPressed: _busy ? null : _addAsset,
        icon: const Icon(Icons.add_box_outlined),
        label: const Text('Add asset'),
      ),
      body: AsyncBody(
        value: inventory,
        onRetry: () => ref.invalidate(adminAssetInventoryProvider),
        isEmpty: (data) => (data['items'] as List? ?? const []).isEmpty,
        emptyTitle: 'No assets',
        emptyMessage: 'Add an asset to start the inventory.',
        emptyIcon: Icons.inventory_2_outlined,
        builder: (data) {
          final rawItems = data['items'];
          final items = rawItems is List
              ? rawItems.whereType<Map>().toList()
              : const [];
          return ListView.separated(
            physics: const AlwaysScrollableScrollPhysics(),
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 90),
            itemCount: items.length + 1,
            separatorBuilder: (_, __) => const SizedBox(height: 8),
            itemBuilder: (context, index) {
              if (index == 0) {
                return Card(
                  child: ListTile(
                    leading: const Icon(Icons.warning_amber_rounded),
                    title: Text(
                      '${data['overdueReturns'] ?? 0} overdue return(s)',
                    ),
                    subtitle: const Text(
                      'Review assets past their return date.',
                    ),
                  ),
                );
              }
              final asset = Map<String, dynamic>.from(items[index - 1]);
              final id = asset['id']?.toString() ?? '';
              final status = asset['status']?.toString() ?? 'Unknown';
              return Card(
                child: ListTile(
                  leading: const Icon(Icons.inventory_2_outlined),
                  title: Text(asset['name']?.toString() ?? 'Asset'),
                  subtitle: Text(
                    [
                      asset['assetType']?.toString() ?? '',
                      asset['assetTag']?.toString() ?? '',
                      asset['custodianEmployeeName']?.toString() ??
                          'Unassigned',
                    ].where((part) => part.isNotEmpty).join(' · '),
                  ),
                  trailing: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      StatusChip(status: status, dense: true),
                      if (status == 'Available')
                        IconButton(
                          tooltip: 'Assign asset',
                          onPressed: _busy ? null : () => _assignAsset(id),
                          icon: const Icon(Icons.assignment_ind_outlined),
                        )
                      else if (status != 'Retired')
                        IconButton(
                          tooltip: 'Manage asset lifecycle',
                          onPressed: _busy ? null : () => _manageAsset(asset),
                          icon: const Icon(Icons.more_horiz_rounded),
                        ),
                    ],
                  ),
                ),
              );
            },
          );
        },
      ),
    );
  }
}
