import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/strings.dart';
import '../../providers/api_provider.dart';
import '../../providers/leave_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';

final _adminLeaveTypesProvider = FutureProvider<List<dynamic>>(
  (ref) => ref.watch(apiClientProvider).getList('/admin/leave-types'),
);
final _adminLeavePoliciesProvider = FutureProvider<List<dynamic>>(
  (ref) => ref.watch(apiClientProvider).getList('/admin/leave-policies'),
);

class AdminLeavePoliciesScreen extends StatelessWidget {
  const AdminLeavePoliciesScreen({super.key});

  @override
  Widget build(BuildContext context) => DefaultTabController(
    length: 2,
    child: AppScaffold(
      title: 'Leave administration',
      bottom: const TabBar(
        tabs: [
          Tab(text: 'Leave types'),
          Tab(text: 'Policies'),
        ],
      ),
      body: const TabBarView(children: [_LeaveTypesTab(), _LeavePoliciesTab()]),
    ),
  );
}

class _LeaveTypesTab extends ConsumerWidget {
  const _LeaveTypesTab();

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
    final maxDays = TextEditingController(
      text: current?['maxConsecutiveDays']?.toString() ?? '',
    );
    final unit = TextEditingController(
      text: current?['unit']?.toString() ?? 'Days',
    );
    final order = TextEditingController(
      text: current?['displayOrder']?.toString() ?? '0',
    );
    var isPaid = current?['isPaid'] != false;
    var attachment = current?['requiresAttachment'] == true;
    var halfDay = current?['allowsHalfDay'] == true;
    var active = current?['isActive'] != false;
    final formKey = GlobalKey<FormState>();
    final saved = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setState) => AlertDialog(
          title: Text(current == null ? 'Add leave type' : 'Edit leave type'),
          content: Form(
            key: formKey,
            child: SingleChildScrollView(
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  _requiredField(code, 'Code'),
                  _requiredField(name, 'Name'),
                  _requiredField(unit, 'Unit'),
                  TextFormField(
                    controller: maxDays,
                    keyboardType: TextInputType.number,
                    decoration: const InputDecoration(
                      labelText: 'Max consecutive days (optional)',
                    ),
                  ),
                  TextFormField(
                    controller: order,
                    keyboardType: TextInputType.number,
                    decoration: const InputDecoration(
                      labelText: 'Display order',
                    ),
                  ),
                  SwitchListTile(
                    contentPadding: EdgeInsets.zero,
                    title: const Text('Paid leave'),
                    value: isPaid,
                    onChanged: (value) => setState(() => isPaid = value),
                  ),
                  SwitchListTile(
                    contentPadding: EdgeInsets.zero,
                    title: const Text('Requires attachment'),
                    value: attachment,
                    onChanged: (value) => setState(() => attachment = value),
                  ),
                  SwitchListTile(
                    contentPadding: EdgeInsets.zero,
                    title: const Text('Allows half day'),
                    value: halfDay,
                    onChanged: (value) => setState(() => halfDay = value),
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
          'unit': unit.text.trim(),
          'maxConsecutiveDays': int.tryParse(maxDays.text.trim()),
          'displayOrder': int.tryParse(order.text.trim()) ?? 0,
          'isPaid': isPaid,
          'requiresAttachment': attachment,
          'allowsHalfDay': halfDay,
          'isActive': active,
        };
        final api = ref.read(apiClientProvider);
        if (current == null) {
          await api.postMap('/admin/leave-types', data: data);
        } else {
          await api.putMap('/admin/leave-types/${current['id']}', data: data);
        }
        ref.invalidate(_adminLeaveTypesProvider);
        ref.invalidate(_adminLeavePoliciesProvider);
        ref.invalidate(leaveTypesProvider);
        if (context.mounted)
          ScaffoldMessenger.of(context).showSnackBar(
            const SnackBar(content: Text(Strings.savedSuccessfully)),
          );
      } catch (error) {
        if (context.mounted)
          ScaffoldMessenger.of(context).showSnackBar(
            SnackBar(content: Text('Leave type update failed: $error')),
          );
      }
    }
    for (final controller in [code, name, maxDays, unit, order]) {
      controller.dispose();
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final types = ref.watch(_adminLeaveTypesProvider);
    return Column(
      children: [
        _addButton('Add leave type', () => _edit(context, ref)),
        Expanded(
          child: AsyncBody(
            value: types,
            onRetry: () => ref.invalidate(_adminLeaveTypesProvider),
            isEmpty: (items) => items.isEmpty,
            emptyTitle: 'No leave types',
            emptyMessage: 'Add a leave type to make it available in policies.',
            builder: (items) => ListView.separated(
              padding: const EdgeInsets.all(12),
              itemCount: items.length,
              separatorBuilder: (_, _) => const SizedBox(height: 8),
              itemBuilder: (context, index) {
                final item = Map<String, dynamic>.from(items[index] as Map);
                return Card(
                  child: ListTile(
                    title: Text('${item['name']} · ${item['code']}'),
                    subtitle: Text(
                      '${item['unit']} · ${item['isPaid'] == true ? 'Paid' : 'Unpaid'} · ${item['isActive'] == true ? 'Active' : 'Inactive'}',
                    ),
                    trailing: IconButton(
                      onPressed: () => _edit(context, ref, current: item),
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

class _LeavePoliciesTab extends ConsumerWidget {
  const _LeavePoliciesTab();

  Future<void> _edit(
    BuildContext context,
    WidgetRef ref, {
    Map<String, dynamic>? current,
  }) async {
    final types = await ref.read(_adminLeaveTypesProvider.future);
    if (!context.mounted) return;
    final code = TextEditingController(
      text: current?['code']?.toString() ?? '',
    );
    final name = TextEditingController(
      text: current?['name']?.toString() ?? '',
    );
    final description = TextEditingController(
      text: current?['description']?.toString() ?? '',
    );
    final annual = TextEditingController(
      text: current?['annualEntitlementDays']?.toString() ?? '0',
    );
    final carry = TextEditingController(
      text: current?['carryForwardMaxDays']?.toString() ?? '0',
    );
    final notice = TextEditingController(
      text: current?['noticeDays']?.toString() ?? '0',
    );
    String? leaveTypeId = current?['leaveTypeId']?.toString();
    var frequency = current?['accrualFrequency']?.toString() ?? 'Monthly';
    var negative = current?['negativeBalanceAllowed'] == true;
    var attachment = current?['attachmentRequired'] == true;
    var active = current?['isActive'] != false;
    final formKey = GlobalKey<FormState>();
    final saved = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setState) => AlertDialog(
          title: Text(
            current == null ? 'Add leave policy' : 'Edit leave policy',
          ),
          content: Form(
            key: formKey,
            child: SingleChildScrollView(
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  _requiredField(code, 'Policy code'),
                  _requiredField(name, 'Policy name'),
                  TextField(
                    controller: description,
                    decoration: const InputDecoration(labelText: 'Description'),
                  ),
                  DropdownButtonFormField<String?>(
                    initialValue: leaveTypeId,
                    decoration: const InputDecoration(labelText: 'Leave type'),
                    items: [
                      const DropdownMenuItem<String?>(
                        value: null,
                        child: Text('All leave types'),
                      ),
                      for (final raw in types.whereType<Map>())
                        if (raw['isActive'] == true)
                          DropdownMenuItem<String?>(
                            value: raw['id']?.toString(),
                            child: Text(raw['name'].toString()),
                          ),
                    ],
                    onChanged: (value) => setState(() => leaveTypeId = value),
                  ),
                  DropdownButtonFormField<String>(
                    initialValue: frequency,
                    decoration: const InputDecoration(
                      labelText: 'Accrual frequency',
                    ),
                    items: const [
                      DropdownMenuItem(value: 'None', child: Text('None')),
                      DropdownMenuItem(
                        value: 'Monthly',
                        child: Text('Monthly'),
                      ),
                      DropdownMenuItem(
                        value: 'Quarterly',
                        child: Text('Quarterly'),
                      ),
                      DropdownMenuItem(value: 'Annual', child: Text('Annual')),
                      DropdownMenuItem(
                        value: 'OnDemand',
                        child: Text('On demand'),
                      ),
                    ],
                    onChanged: (value) =>
                        setState(() => frequency = value ?? frequency),
                  ),
                  _numberField(annual, 'Annual entitlement (days)'),
                  _numberField(carry, 'Carry-forward maximum (days)'),
                  _numberField(notice, 'Notice days', integer: true),
                  SwitchListTile(
                    contentPadding: EdgeInsets.zero,
                    title: const Text('Allow negative balance'),
                    value: negative,
                    onChanged: (value) => setState(() => negative = value),
                  ),
                  SwitchListTile(
                    contentPadding: EdgeInsets.zero,
                    title: const Text('Require attachment'),
                    value: attachment,
                    onChanged: (value) => setState(() => attachment = value),
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
          'description': description.text.trim().isEmpty
              ? null
              : description.text.trim(),
          'leaveTypeId': leaveTypeId,
          'accrualFrequency': frequency,
          'annualEntitlementDays': double.tryParse(annual.text.trim()) ?? 0,
          'carryForwardMaxDays': double.tryParse(carry.text.trim()) ?? 0,
          'noticeDays': int.tryParse(notice.text.trim()) ?? 0,
          'negativeBalanceAllowed': negative,
          'attachmentRequired': attachment,
          'isActive': active,
        };
        final api = ref.read(apiClientProvider);
        if (current == null) {
          await api.postMap('/admin/leave-policies', data: data);
        } else {
          await api.putMap(
            '/admin/leave-policies/${current['id']}',
            data: data,
          );
        }
        ref.invalidate(_adminLeavePoliciesProvider);
        if (context.mounted)
          ScaffoldMessenger.of(context).showSnackBar(
            const SnackBar(content: Text(Strings.savedSuccessfully)),
          );
      } catch (error) {
        if (context.mounted)
          ScaffoldMessenger.of(context).showSnackBar(
            SnackBar(content: Text('Leave policy update failed: $error')),
          );
      }
    }
    for (final controller in [code, name, description, annual, carry, notice]) {
      controller.dispose();
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final policies = ref.watch(_adminLeavePoliciesProvider);
    return Column(
      children: [
        _addButton('Add policy', () => _edit(context, ref)),
        Expanded(
          child: AsyncBody(
            value: policies,
            onRetry: () => ref.invalidate(_adminLeavePoliciesProvider),
            isEmpty: (items) => items.isEmpty,
            emptyTitle: 'No leave policies',
            emptyMessage: 'Set accrual, carry-forward and notice rules.',
            builder: (items) => ListView.separated(
              padding: const EdgeInsets.all(12),
              itemCount: items.length,
              separatorBuilder: (_, _) => const SizedBox(height: 8),
              itemBuilder: (context, index) {
                final item = Map<String, dynamic>.from(items[index] as Map);
                return Card(
                  child: ListTile(
                    title: Text('${item['name']} · ${item['code']}'),
                    subtitle: Text(
                      '${item['annualEntitlementDays']} days/year · ${item['accrualFrequency']} · ${item['isActive'] == true ? 'Active' : 'Inactive'}',
                    ),
                    trailing: IconButton(
                      onPressed: () => _edit(context, ref, current: item),
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

Widget _addButton(String label, VoidCallback onPressed) => Padding(
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

Widget _numberField(
  TextEditingController controller,
  String label, {
  bool integer = false,
}) => Padding(
  padding: const EdgeInsets.only(top: 8),
  child: TextFormField(
    controller: controller,
    keyboardType: TextInputType.numberWithOptions(decimal: !integer),
    decoration: InputDecoration(labelText: label),
    validator: (value) {
      final parsed = integer
          ? int.tryParse(value ?? '')
          : double.tryParse(value ?? '');
      return parsed == null || parsed < 0
          ? 'Enter a non-negative number'
          : null;
    },
  ),
);
