import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/network/api_exception.dart';
import '../../core/strings.dart';
import '../../core/utils/formatters.dart';
import '../../providers/api_provider.dart';
import '../../providers/auth_provider.dart';
import '../../providers/directory_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';
import '../../widgets/section_card.dart';
import '../../widgets/status_chip.dart';

/// Employee record (own profile or `employee.view` scope). The server only
/// returns fields the caller may see (FR-EMP-024 privacy rules).
class EmployeeDetailScreen extends ConsumerStatefulWidget {
  const EmployeeDetailScreen({super.key, required this.employeeId});

  final String employeeId;

  @override
  ConsumerState<EmployeeDetailScreen> createState() =>
      _EmployeeDetailScreenState();
}

class _EmployeeDetailScreenState extends ConsumerState<EmployeeDetailScreen> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) {
        ref.read(authControllerProvider.notifier).refreshCurrentUser();
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    final detail = ref.watch(employeeDetailProvider(widget.employeeId));
    final theme = Theme.of(context);
    final auth = ref.watch(authStateProvider);
    final canEdit = auth.hasPermission('employee.edit');
    final canManageStatus = auth.hasPermission('employee.manage');

    return AppScaffold(
      title: Strings.employeeDetail,
      actions: [
        if (canEdit || canManageStatus)
          PopupMenuButton<String>(
            tooltip: 'Employee actions',
            onSelected: (action) async {
              final profile = detail.asData?.value;
              if (profile == null) return;
              if (action == 'edit') {
                await _editEmployee(context, ref, profile);
              } else if (action == 'status') {
                await _changeStatus(context, ref, profile);
              }
            },
            itemBuilder: (context) => [
              if (canEdit)
                const PopupMenuItem(
                  value: 'edit',
                  child: Text('Edit employee'),
                ),
              if (canManageStatus)
                const PopupMenuItem(
                  value: 'status',
                  child: Text('Change employment status'),
                ),
            ],
          ),
      ],
      body: RefreshIndicator(
        onRefresh: () async =>
            ref.invalidate(employeeDetailProvider(widget.employeeId)),
        child: AsyncBody(
          value: detail,
          onRetry: () =>
              ref.invalidate(employeeDetailProvider(widget.employeeId)),
          builder: (profile) => SingleChildScrollView(
            physics: const AlwaysScrollableScrollPhysics(),
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    CircleAvatar(
                      radius: 30,
                      backgroundColor: theme.colorScheme.primaryContainer,
                      foregroundColor: theme.colorScheme.onPrimaryContainer,
                      child: Text(
                        profile.initials,
                        style: const TextStyle(
                          fontWeight: FontWeight.w800,
                          fontSize: 20,
                        ),
                      ),
                    ),
                    const SizedBox(width: 14),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            profile.displayName,
                            style: theme.textTheme.titleLarge?.copyWith(
                              fontWeight: FontWeight.w700,
                            ),
                          ),
                          if (profile.designation != null)
                            Text(
                              profile.designation!,
                              style: theme.textTheme.bodyMedium?.copyWith(
                                color: theme.colorScheme.onSurfaceVariant,
                              ),
                            ),
                        ],
                      ),
                    ),
                    if (profile.employmentStatus != null)
                      StatusChip(status: profile.employmentStatus, dense: true),
                  ],
                ),
                const SizedBox(height: 16),
                SectionCard(
                  title: 'Work',
                  child: Column(
                    children: [
                      InfoRow(
                        label: 'Employee number',
                        value: profile.employeeNumber,
                      ),
                      InfoRow(
                        label: Strings.department,
                        value: profile.department ?? '-',
                      ),
                      InfoRow(
                        label: Strings.location,
                        value: profile.location ?? '-',
                      ),
                      InfoRow(
                        label: Strings.designation,
                        value: profile.designation ?? '-',
                      ),
                      InfoRow(
                        label: Strings.manager,
                        value: profile.managerName ?? '-',
                      ),
                      InfoRow(
                        label: Strings.workEmail,
                        value: profile.workEmail ?? '-',
                      ),
                      InfoRow(
                        label: Strings.joiningDate,
                        value: AppFormatters.date(profile.joiningDate),
                      ),
                      InfoRow(
                        label: Strings.employmentType,
                        value: AppFormatters.enumLabel(profile.employmentType),
                      ),
                      InfoRow(
                        label: Strings.legalEntity,
                        value: profile.legalEntity ?? '-',
                      ),
                      InfoRow(
                        label: Strings.costCentre,
                        value: profile.costCentre ?? '-',
                      ),
                    ],
                  ),
                ),
                const SizedBox(height: 12),
                SectionCard(
                  title: 'Contact',
                  child: Column(
                    children: [
                      InfoRow(
                        label: Strings.phone,
                        value: profile.phone ?? Strings.noPhone,
                      ),
                      InfoRow(
                        label: Strings.personalEmail,
                        value: profile.personalEmail ?? '-',
                      ),
                      InfoRow(
                        label: Strings.address,
                        value: profile.address ?? '-',
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

Future<void> _editEmployee(
  BuildContext context,
  WidgetRef ref,
  dynamic profile,
) async {
  late final EmployeeCreateOptions options;
  try {
    options = await ref.read(employeeCreateOptionsProvider.future);
  } catch (error) {
    if (context.mounted) {
      final message = error is ApiException ? error.message : error.toString();
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text('Could not load employee edit options: $message'),
        ),
      );
    }
    return;
  }
  if (!context.mounted) return;
  final nameParts = profile.displayName.trim().split(RegExp(r'\s+'));
  final firstName = TextEditingController(text: nameParts.firstOrNull ?? '');
  final lastName = TextEditingController(
    text: nameParts.length > 1 ? nameParts.skip(1).join(' ') : '',
  );
  final email = TextEditingController(text: profile.workEmail ?? '');
  final phone = TextEditingController(text: profile.phone ?? '');
  final costCentre = TextEditingController(text: profile.costCentre ?? '');
  final effectiveDate = TextEditingController(
    text: DateTime.now().toIso8601String().substring(0, 10),
  );
  final reason = TextEditingController();
  String? departmentId = profile.departmentId;
  String? locationId = profile.locationId;
  String? designationId;
  String? managerId = profile.managerEmployeeId;
  String employmentType = profile.employmentType ?? 'FullTime';
  final formKey = GlobalKey<FormState>();

  final saved = await showDialog<bool>(
    context: context,
    builder: (dialogContext) => StatefulBuilder(
      builder: (dialogContext, setDialogState) => AlertDialog(
        title: Text('Edit ${profile.employeeNumber}'),
        content: SizedBox(
          width: 480,
          child: Form(
            key: formKey,
            child: SingleChildScrollView(
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  TextFormField(
                    controller: firstName,
                    decoration: const InputDecoration(labelText: 'First name'),
                    validator: _requiredEmployeeField,
                  ),
                  TextFormField(
                    controller: lastName,
                    decoration: const InputDecoration(labelText: 'Last name'),
                    validator: _requiredEmployeeField,
                  ),
                  TextFormField(
                    controller: email,
                    keyboardType: TextInputType.emailAddress,
                    decoration: const InputDecoration(
                      labelText: Strings.workEmail,
                    ),
                    validator: _requiredEmployeeField,
                  ),
                  TextFormField(
                    controller: phone,
                    keyboardType: TextInputType.phone,
                    decoration: const InputDecoration(labelText: Strings.phone),
                  ),
                  const SizedBox(height: 10),
                  DropdownButtonFormField<String?>(
                    initialValue: departmentId,
                    decoration: const InputDecoration(
                      labelText: Strings.department,
                    ),
                    items: [
                      const DropdownMenuItem<String?>(
                        value: null,
                        child: Text('Unassigned'),
                      ),
                      if (departmentId != null &&
                          !options.departments.any(
                            (item) => item.id == departmentId,
                          ))
                        DropdownMenuItem<String?>(
                          value: departmentId,
                          child: Text(
                            profile.department ?? 'Current department',
                          ),
                        ),
                      for (final item in options.departments)
                        DropdownMenuItem<String?>(
                          value: item.id,
                          child: Text(item.name),
                        ),
                    ],
                    onChanged: (value) =>
                        setDialogState(() => departmentId = value),
                  ),
                  const SizedBox(height: 10),
                  DropdownButtonFormField<String?>(
                    initialValue: locationId,
                    decoration: const InputDecoration(
                      labelText: Strings.location,
                    ),
                    items: [
                      const DropdownMenuItem<String?>(
                        value: null,
                        child: Text('Unassigned'),
                      ),
                      if (locationId != null &&
                          !options.locations.any(
                            (item) => item.id == locationId,
                          ))
                        DropdownMenuItem<String?>(
                          value: locationId,
                          child: Text(profile.location ?? 'Current location'),
                        ),
                      for (final item in options.locations)
                        DropdownMenuItem<String?>(
                          value: item.id,
                          child: Text(item.name),
                        ),
                    ],
                    onChanged: (value) =>
                        setDialogState(() => locationId = value),
                  ),
                  const SizedBox(height: 10),
                  DropdownButtonFormField<String?>(
                    initialValue: designationId,
                    decoration: const InputDecoration(
                      labelText: Strings.designation,
                    ),
                    items: [
                      const DropdownMenuItem<String?>(
                        value: null,
                        child: Text('Keep current designation'),
                      ),
                      for (final item in options.designations)
                        DropdownMenuItem<String?>(
                          value: item.id,
                          child: Text(item.name),
                        ),
                    ],
                    onChanged: (value) =>
                        setDialogState(() => designationId = value),
                  ),
                  const SizedBox(height: 10),
                  DropdownButtonFormField<String?>(
                    initialValue: managerId,
                    decoration: const InputDecoration(
                      labelText: Strings.manager,
                    ),
                    items: [
                      const DropdownMenuItem<String?>(
                        value: null,
                        child: Text('No manager'),
                      ),
                      if (managerId != null &&
                          !options.managers.any(
                            (item) => item.employeeId == managerId,
                          ))
                        DropdownMenuItem<String?>(
                          value: managerId,
                          child: Text(profile.managerName ?? 'Current manager'),
                        ),
                      for (final item in options.managers.where(
                        (item) => item.employeeId != profile.employeeId,
                      ))
                        DropdownMenuItem<String?>(
                          value: item.employeeId,
                          child: Text(item.label),
                        ),
                    ],
                    onChanged: (value) =>
                        setDialogState(() => managerId = value),
                  ),
                  const SizedBox(height: 10),
                  DropdownButtonFormField<String>(
                    initialValue: employmentType,
                    decoration: const InputDecoration(
                      labelText: Strings.employmentType,
                    ),
                    items: const [
                      DropdownMenuItem(
                        value: 'FullTime',
                        child: Text('Full time'),
                      ),
                      DropdownMenuItem(
                        value: 'PartTime',
                        child: Text('Part time'),
                      ),
                      DropdownMenuItem(
                        value: 'Contract',
                        child: Text('Contract'),
                      ),
                      DropdownMenuItem(value: 'Intern', child: Text('Intern')),
                    ],
                    onChanged: (value) => setDialogState(
                      () => employmentType = value ?? employmentType,
                    ),
                  ),
                  TextFormField(
                    controller: costCentre,
                    decoration: const InputDecoration(
                      labelText: Strings.costCentre,
                    ),
                  ),
                  TextFormField(
                    controller: effectiveDate,
                    decoration: const InputDecoration(
                      labelText: 'Effective date · YYYY-MM-DD',
                    ),
                    validator: _requiredEmployeeField,
                  ),
                  TextFormField(
                    controller: reason,
                    maxLength: 500,
                    decoration: const InputDecoration(
                      labelText: 'Reason for change',
                    ),
                    validator: _requiredEmployeeField,
                  ),
                ],
              ),
            ),
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(dialogContext, false),
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
      await ref
          .read(apiClientProvider)
          .putMap(
            '/employees/${profile.employeeId}',
            data: {
              'firstName': firstName.text.trim(),
              'lastName': lastName.text.trim(),
              'workEmail': email.text.trim(),
              if (phone.text.trim().isNotEmpty) 'phone': phone.text.trim(),
              'clearPhone': phone.text.trim().isEmpty && profile.phone != null,
              if (departmentId != null) 'departmentId': departmentId,
              if (locationId != null) 'locationId': locationId,
              if (designationId != null) 'designationId': designationId,
              'managerId': managerId,
              'clearManager':
                  managerId == null && profile.managerEmployeeId != null,
              'employmentType': employmentType,
              if (costCentre.text.trim().isNotEmpty)
                'costCentre': costCentre.text.trim(),
              'clearCostCentre':
                  costCentre.text.trim().isEmpty && profile.costCentre != null,
              'effectiveDate': effectiveDate.text.trim(),
              'reason': reason.text.trim(),
            },
          );
      ref.invalidate(employeeDetailProvider(profile.employeeId));
      ref.invalidate(directoryProvider);
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text(Strings.savedSuccessfully)),
        );
      }
    } catch (error) {
      if (context.mounted) {
        final message = error is ApiException
            ? error.message
            : error.toString();
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Employee update failed: $message')),
        );
      }
    }
  }
  for (final controller in [
    firstName,
    lastName,
    email,
    phone,
    costCentre,
    effectiveDate,
    reason,
  ]) {
    controller.dispose();
  }
}

Future<void> _changeStatus(
  BuildContext context,
  WidgetRef ref,
  dynamic profile,
) async {
  var status = switch (profile.employmentStatus) {
    'Suspended' => 'Suspended',
    'Terminated' => 'Terminated',
    _ => 'Active',
  };
  final reason = TextEditingController();
  final effectiveDate = TextEditingController(
    text: DateTime.now().toIso8601String().substring(0, 10),
  );
  final formKey = GlobalKey<FormState>();
  final saved = await showDialog<bool>(
    context: context,
    builder: (dialogContext) => StatefulBuilder(
      builder: (dialogContext, setDialogState) => AlertDialog(
        title: const Text('Change employment status'),
        content: Form(
          key: formKey,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              DropdownButtonFormField<String>(
                initialValue: status,
                decoration: const InputDecoration(labelText: 'Status'),
                items: const [
                  DropdownMenuItem(value: 'Active', child: Text('Active')),
                  DropdownMenuItem(
                    value: 'Suspended',
                    child: Text('Suspended'),
                  ),
                  DropdownMenuItem(
                    value: 'Terminated',
                    child: Text('Terminated'),
                  ),
                ],
                onChanged: (value) =>
                    setDialogState(() => status = value ?? status),
              ),
              TextFormField(
                controller: effectiveDate,
                decoration: const InputDecoration(
                  labelText: 'Effective date · YYYY-MM-DD',
                ),
                validator: _requiredEmployeeField,
              ),
              TextFormField(
                controller: reason,
                maxLength: 500,
                decoration: const InputDecoration(labelText: 'Reason'),
                validator: _requiredEmployeeField,
              ),
            ],
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(dialogContext, false),
            child: const Text(Strings.cancel),
          ),
          FilledButton(
            onPressed: () {
              if (formKey.currentState?.validate() ?? false)
                Navigator.pop(dialogContext, true);
            },
            child: const Text('Update status'),
          ),
        ],
      ),
    ),
  );
  if (saved == true && context.mounted) {
    try {
      await ref
          .read(apiClientProvider)
          .postMap(
            '/employees/${profile.employeeId}/status',
            data: {
              'status': status,
              'effectiveDate': effectiveDate.text.trim(),
              'reason': reason.text.trim(),
            },
          );
      ref.invalidate(employeeDetailProvider(profile.employeeId));
      ref.invalidate(directoryProvider);
      if (context.mounted)
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Employment status updated')),
        );
    } catch (error) {
      if (context.mounted)
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text('Status update failed: $error')));
    }
  }
  reason.dispose();
  effectiveDate.dispose();
}

String? _requiredEmployeeField(String? value) =>
    value == null || value.trim().isEmpty ? Strings.fieldRequired : null;
