import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/network/api_exception.dart';
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
    length: 3,
    child: AppScaffold(
      title: 'Leave administration',
      bottom: const TabBar(
        isScrollable: true,
        tabs: [
          Tab(text: 'Leave types'),
          Tab(text: 'Policies'),
          Tab(text: 'Balances'),
        ],
      ),
      body: const TabBarView(
        children: [_LeaveTypesTab(), _LeavePoliciesTab(), _LeaveBalancesTab()],
      ),
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
    var saving = false;
    String? formError;
    Map<String, String> fieldErrors = {};
    final saved = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) {
          Future<void> save() async {
            if (!(formKey.currentState?.validate() ?? false)) return;
            setDialogState(() {
              saving = true;
              formError = null;
              fieldErrors = {};
            });
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
                await api.putMap(
                  '/admin/leave-types/${current['id']}',
                  data: data,
                );
              }
              if (dialogContext.mounted) Navigator.pop(dialogContext, true);
            } on ApiException catch (error) {
              setDialogState(() {
                fieldErrors = _fieldErrorsFrom(error);
                formError = error.errors.isEmpty ? error.message : error.detail;
                saving = false;
              });
            } catch (error) {
              setDialogState(() {
                formError = 'Could not save leave type: $error';
                saving = false;
              });
            }
          }

          return AlertDialog(
            title: Text(current == null ? 'Add leave type' : 'Edit leave type'),
            content: Form(
              key: formKey,
              child: SingleChildScrollView(
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    if (formError != null) _formError(formError!),
                    _requiredField(
                      code,
                      'Code',
                      errorText: _fieldError(fieldErrors, 'code'),
                    ),
                    _requiredField(
                      name,
                      'Name',
                      errorText: _fieldError(fieldErrors, 'name'),
                    ),
                    _requiredField(
                      unit,
                      'Unit',
                      errorText: _fieldError(fieldErrors, 'unit'),
                    ),
                    TextFormField(
                      controller: maxDays,
                      keyboardType: TextInputType.number,
                      decoration: InputDecoration(
                        labelText: 'Max consecutive days (optional)',
                        errorText: _fieldError(
                          fieldErrors,
                          'maxConsecutiveDays',
                        ),
                      ),
                      validator: (value) {
                        if (value == null || value.trim().isEmpty) return null;
                        final days = int.tryParse(value.trim());
                        return days == null || days < 1 || days > 365
                            ? 'Enter a whole number from 1 to 365.'
                            : null;
                      },
                    ),
                    TextFormField(
                      controller: order,
                      keyboardType: TextInputType.number,
                      decoration: InputDecoration(
                        labelText: 'Display order',
                        errorText: _fieldError(fieldErrors, 'displayOrder'),
                      ),
                      validator: (value) => int.tryParse(value ?? '') == null
                          ? 'Enter a whole number.'
                          : null,
                    ),
                    SwitchListTile(
                      contentPadding: EdgeInsets.zero,
                      title: const Text('Paid leave'),
                      value: isPaid,
                      onChanged: saving
                          ? null
                          : (value) => setDialogState(() => isPaid = value),
                    ),
                    SwitchListTile(
                      contentPadding: EdgeInsets.zero,
                      title: const Text('Requires attachment'),
                      value: attachment,
                      onChanged: saving
                          ? null
                          : (value) => setDialogState(() => attachment = value),
                    ),
                    SwitchListTile(
                      contentPadding: EdgeInsets.zero,
                      title: const Text('Allows half day'),
                      value: halfDay,
                      onChanged: saving
                          ? null
                          : (value) => setDialogState(() => halfDay = value),
                    ),
                    SwitchListTile(
                      contentPadding: EdgeInsets.zero,
                      title: const Text('Active'),
                      value: active,
                      onChanged: saving
                          ? null
                          : (value) => setDialogState(() => active = value),
                    ),
                  ],
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
      ref.invalidate(_adminLeaveTypesProvider);
      ref.invalidate(_adminLeavePoliciesProvider);
      ref.invalidate(leaveTypesProvider);
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(const SnackBar(content: Text(Strings.savedSuccessfully)));
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
    late final List<dynamic> types;
    try {
      types = await ref.read(_adminLeaveTypesProvider.future);
    } catch (error) {
      if (context.mounted) {
        final message = error is ApiException
            ? error.message
            : error.toString();
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Could not load leave types: $message')),
        );
      }
      return;
    }
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
    var saving = false;
    String? formError;
    Map<String, String> fieldErrors = {};
    final saved = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) {
          Future<void> save() async {
            if (!(formKey.currentState?.validate() ?? false)) return;
            setDialogState(() {
              saving = true;
              formError = null;
              fieldErrors = {};
            });
            try {
              final data = {
                'code': code.text.trim(),
                'name': name.text.trim(),
                'description': description.text.trim().isEmpty
                    ? null
                    : description.text.trim(),
                'leaveTypeId': leaveTypeId,
                'accrualFrequency': frequency,
                'annualEntitlementDays':
                    double.tryParse(annual.text.trim()) ?? 0,
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
              if (dialogContext.mounted) Navigator.pop(dialogContext, true);
            } on ApiException catch (error) {
              setDialogState(() {
                fieldErrors = _fieldErrorsFrom(error);
                formError = error.errors.isEmpty ? error.message : error.detail;
                saving = false;
              });
            } catch (error) {
              setDialogState(() {
                formError = 'Could not save leave policy: $error';
                saving = false;
              });
            }
          }

          return AlertDialog(
            title: Text(
              current == null ? 'Add leave policy' : 'Edit leave policy',
            ),
            content: Form(
              key: formKey,
              child: SingleChildScrollView(
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    if (formError != null) _formError(formError!),
                    _requiredField(
                      code,
                      'Policy code',
                      errorText: _fieldError(fieldErrors, 'code'),
                    ),
                    _requiredField(
                      name,
                      'Policy name',
                      errorText: _fieldError(fieldErrors, 'name'),
                    ),
                    TextFormField(
                      controller: description,
                      maxLength: 500,
                      decoration: InputDecoration(
                        labelText: 'Description',
                        errorText: _fieldError(fieldErrors, 'description'),
                      ),
                      validator: (value) => value != null && value.length > 500
                          ? 'Description must be 500 characters or fewer.'
                          : null,
                    ),
                    DropdownButtonFormField<String?>(
                      initialValue: leaveTypeId,
                      decoration: InputDecoration(
                        labelText: 'Leave type',
                        errorText: _fieldError(fieldErrors, 'leaveTypeId'),
                      ),
                      items: [
                        const DropdownMenuItem<String?>(
                          value: null,
                          child: Text('All leave types'),
                        ),
                        for (final raw in types.whereType<Map>())
                          if (raw['isActive'] == true ||
                              raw['id']?.toString() == leaveTypeId)
                            DropdownMenuItem<String?>(
                              value: raw['id']?.toString(),
                              child: Text(raw['name'].toString()),
                            ),
                      ],
                      onChanged: saving
                          ? null
                          : (value) =>
                                setDialogState(() => leaveTypeId = value),
                    ),
                    DropdownButtonFormField<String>(
                      initialValue: frequency,
                      decoration: InputDecoration(
                        labelText: 'Accrual frequency',
                        errorText: _fieldError(fieldErrors, 'accrualFrequency'),
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
                        DropdownMenuItem(
                          value: 'Annual',
                          child: Text('Annual'),
                        ),
                        DropdownMenuItem(
                          value: 'OnDemand',
                          child: Text('On demand'),
                        ),
                      ],
                      onChanged: saving
                          ? null
                          : (value) => setDialogState(
                              () => frequency = value ?? frequency,
                            ),
                    ),
                    _numberField(
                      annual,
                      'Annual entitlement (days)',
                      errorText: _fieldError(
                        fieldErrors,
                        'annualEntitlementDays',
                      ),
                    ),
                    _numberField(
                      carry,
                      'Carry-forward maximum (days)',
                      errorText: _fieldError(
                        fieldErrors,
                        'carryForwardMaxDays',
                      ),
                    ),
                    _numberField(
                      notice,
                      'Notice days',
                      integer: true,
                      errorText: _fieldError(fieldErrors, 'noticeDays'),
                    ),
                    SwitchListTile(
                      contentPadding: EdgeInsets.zero,
                      title: const Text('Allow negative balance'),
                      value: negative,
                      onChanged: saving
                          ? null
                          : (value) => setDialogState(() => negative = value),
                    ),
                    SwitchListTile(
                      contentPadding: EdgeInsets.zero,
                      title: const Text('Require attachment'),
                      value: attachment,
                      onChanged: saving
                          ? null
                          : (value) => setDialogState(() => attachment = value),
                    ),
                    SwitchListTile(
                      contentPadding: EdgeInsets.zero,
                      title: const Text('Active'),
                      value: active,
                      onChanged: saving
                          ? null
                          : (value) => setDialogState(() => active = value),
                    ),
                  ],
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
      ref.invalidate(_adminLeavePoliciesProvider);
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(const SnackBar(content: Text(Strings.savedSuccessfully)));
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

final _adminEmployeesProvider = FutureProvider<List<Map<String, dynamic>>>((
  ref,
) async {
  final page = await ref
      .watch(apiClientProvider)
      .getMap('/employees', query: {'page': 1, 'pageSize': 500});
  final items = (page['items'] as List?) ?? const [];
  return items
      .whereType<Map>()
      .map((e) => Map<String, dynamic>.from(e))
      .toList();
});

/// Admin/HR: add or set an employee's leave balance (FR-ADM-054).
class _LeaveBalancesTab extends ConsumerStatefulWidget {
  const _LeaveBalancesTab();

  @override
  ConsumerState<_LeaveBalancesTab> createState() => _LeaveBalancesTabState();
}

class _LeaveBalancesTabState extends ConsumerState<_LeaveBalancesTab> {
  String? _employeeId;
  String? _leaveTypeId;
  final _year = TextEditingController(text: DateTime.now().year.toString());
  final _amount = TextEditingController();
  String _mode = 'Add';
  final _note = TextEditingController();
  bool _saving = false;
  String? _message;
  List<dynamic>? _currentBalances;

  @override
  void dispose() {
    _year.dispose();
    _amount.dispose();
    _note.dispose();
    super.dispose();
  }

  Future<void> _loadBalances() async {
    if (_employeeId == null) return;
    try {
      final list = await ref
          .read(apiClientProvider)
          .getList(
            '/admin/leave/balances',
            query: {'employeeId': _employeeId, 'year': _year.text.trim()},
          );
      setState(() => _currentBalances = list);
    } catch (_) {
      setState(() => _currentBalances = null);
    }
  }

  Future<void> _save() async {
    if (_employeeId == null || _leaveTypeId == null) {
      setState(() => _message = 'Select an employee and leave type.');
      return;
    }
    final amount = double.tryParse(_amount.text.trim());
    if (amount == null) {
      setState(() => _message = 'Enter a valid amount.');
      return;
    }
    setState(() {
      _saving = true;
      _message = null;
    });
    try {
      await ref
          .read(leaveActionsProvider)
          .adjustBalance(
            employeeId: _employeeId!,
            leaveTypeId: _leaveTypeId!,
            year: int.tryParse(_year.text.trim()) ?? DateTime.now().year,
            amount: amount,
            mode: _mode,
            note: _note.text.trim().isEmpty ? null : _note.text.trim(),
          );
      _amount.clear();
      _note.clear();
      await _loadBalances();
      if (mounted) {
        setState(() => _message = 'Balance updated successfully.');
      }
    } on ApiException catch (e) {
      setState(() => _message = e.detail ?? e.message);
    } catch (e) {
      setState(() => _message = 'Could not update: $e');
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final employees = ref.watch(_adminEmployeesProvider);
    final types = ref.watch(leaveTypesProvider);

    return ListView(
      padding: const EdgeInsets.all(16),
      children: [
        Text(
          'Add or set leave balance',
          style: Theme.of(context).textTheme.titleMedium,
        ),
        const SizedBox(height: 4),
        Text(
          'Credit (Add) or overwrite (Set) an employee\u2019s accrued leave for a type and year.',
          style: Theme.of(context).textTheme.bodySmall,
        ),
        const SizedBox(height: 16),
        employees.when(
          data: (list) => DropdownButtonFormField<String>(
            initialValue: _employeeId,
            isExpanded: true,
            decoration: const InputDecoration(
              labelText: 'Employee',
              isDense: true,
            ),
            items: [
              for (final e in list)
                DropdownMenuItem(
                  value: e['id']?.toString(),
                  child: Text(
                    '${e['displayName'] ?? e['firstName'] ?? ''} '
                    '(${e['employeeNumber'] ?? ''})',
                    overflow: TextOverflow.ellipsis,
                  ),
                ),
            ],
            onChanged: (v) {
              setState(() => _employeeId = v);
              _loadBalances();
            },
          ),
          loading: () => const LinearProgressIndicator(),
          error: (_, _) => const Text('Could not load employees.'),
        ),
        const SizedBox(height: 12),
        types.when(
          data: (list) => DropdownButtonFormField<String>(
            initialValue: _leaveTypeId,
            isExpanded: true,
            decoration: const InputDecoration(
              labelText: 'Leave type',
              isDense: true,
            ),
            items: [
              for (final t in list)
                DropdownMenuItem(
                  value: t.id,
                  child: Text('${t.name} (${t.code})'),
                ),
            ],
            onChanged: (v) => setState(() => _leaveTypeId = v),
          ),
          loading: () => const LinearProgressIndicator(),
          error: (_, _) => const Text('Could not load leave types.'),
        ),
        const SizedBox(height: 12),
        Row(
          children: [
            Expanded(
              child: TextField(
                controller: _year,
                keyboardType: TextInputType.number,
                decoration: const InputDecoration(
                  labelText: 'Year',
                  isDense: true,
                ),
                onSubmitted: (_) => _loadBalances(),
              ),
            ),
            const SizedBox(width: 10),
            Expanded(
              child: TextField(
                controller: _amount,
                keyboardType: const TextInputType.numberWithOptions(
                  decimal: true,
                  signed: true,
                ),
                decoration: InputDecoration(
                  labelText: _mode == 'Add' ? 'Days to add' : 'Set days to',
                  isDense: true,
                ),
              ),
            ),
          ],
        ),
        const SizedBox(height: 12),
        SegmentedButton<String>(
          segments: const [
            ButtonSegment(value: 'Add', label: Text('Add / credit')),
            ButtonSegment(value: 'Set', label: Text('Set exact')),
          ],
          selected: {_mode},
          onSelectionChanged: (s) => setState(() => _mode = s.first),
        ),
        const SizedBox(height: 12),
        TextField(
          controller: _note,
          decoration: const InputDecoration(
            labelText: 'Note (optional)',
            isDense: true,
          ),
          minLines: 1,
          maxLines: 2,
        ),
        const SizedBox(height: 12),
        if (_message != null)
          Padding(
            padding: const EdgeInsets.only(bottom: 8),
            child: Text(
              _message!,
              style: TextStyle(
                color: _message!.contains('success')
                    ? Colors.green
                    : Theme.of(context).colorScheme.error,
              ),
            ),
          ),
        FilledButton.icon(
          onPressed: _saving ? null : _save,
          icon: _saving
              ? const SizedBox(
                  width: 18,
                  height: 18,
                  child: CircularProgressIndicator(strokeWidth: 2),
                )
              : const Icon(Icons.add_card_outlined),
          label: Text(_mode == 'Add' ? 'Add balance' : 'Set balance'),
        ),
        if (_currentBalances != null) ...[
          const Divider(height: 32),
          Text(
            'Current balances (${_year.text})',
            style: Theme.of(context).textTheme.titleSmall,
          ),
          const SizedBox(height: 8),
          for (final b in _currentBalances!)
            ListTile(
              dense: true,
              contentPadding: EdgeInsets.zero,
              title: Text('${b['name']} (${b['code']})'),
              subtitle: Text(
                'Accrued ${b['accrued']} \u00b7 Used ${b['used']} \u00b7 '
                'Pending ${b['pending']} \u00b7 Carry ${b['carryForward']}',
              ),
              trailing: Text(
                'Avail ${b['available']}',
                style: const TextStyle(fontWeight: FontWeight.bold),
              ),
            ),
        ],
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

Map<String, String> _fieldErrorsFrom(ApiException error) => {
  for (final entry in error.errors.entries)
    if (entry.value.isNotEmpty) entry.key: entry.value.first,
};

String? _fieldError(Map<String, String> errors, String field) {
  for (final entry in errors.entries) {
    if (entry.key.toLowerCase() == field.toLowerCase()) return entry.value;
  }
  return null;
}

Widget _formError(String message) => Padding(
  padding: const EdgeInsets.only(bottom: 12),
  child: Container(
    width: double.infinity,
    padding: const EdgeInsets.all(10),
    decoration: BoxDecoration(
      color: Colors.red.withValues(alpha: 0.08),
      borderRadius: BorderRadius.circular(8),
    ),
    child: Text(message, style: const TextStyle(color: Colors.red)),
  ),
);

Widget _requiredField(
  TextEditingController controller,
  String label, {
  String? errorText,
}) => Padding(
  padding: const EdgeInsets.only(bottom: 8),
  child: TextFormField(
    controller: controller,
    decoration: InputDecoration(labelText: label, errorText: errorText),
    validator: (value) =>
        value == null || value.trim().isEmpty ? Strings.fieldRequired : null,
  ),
);

Widget _numberField(
  TextEditingController controller,
  String label, {
  bool integer = false,
  String? errorText,
  num max = 365,
}) => Padding(
  padding: const EdgeInsets.only(top: 8),
  child: TextFormField(
    controller: controller,
    keyboardType: TextInputType.numberWithOptions(decimal: !integer),
    decoration: InputDecoration(labelText: label, errorText: errorText),
    validator: (value) {
      final parsed = integer
          ? int.tryParse(value ?? '')
          : double.tryParse(value ?? '');
      if (parsed == null) return 'Enter a valid number.';
      if (parsed < 0 || parsed > max) return 'Enter a number from 0 to $max.';
      return null;
    },
  ),
);
