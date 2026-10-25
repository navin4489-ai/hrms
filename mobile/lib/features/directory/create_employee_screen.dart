import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter/services.dart';
import 'package:go_router/go_router.dart';

import '../../core/network/api_exception.dart';
import '../../core/strings.dart';
import '../../core/utils/validators.dart';
import '../../providers/api_provider.dart';
import '../../providers/directory_provider.dart';
import '../../widgets/app_button.dart';
import '../../widgets/app_scaffold.dart';

class CreateEmployeeScreen extends ConsumerStatefulWidget {
  const CreateEmployeeScreen({super.key});

  @override
  ConsumerState<CreateEmployeeScreen> createState() =>
      _CreateEmployeeScreenState();
}

class _CreateEmployeeScreenState extends ConsumerState<CreateEmployeeScreen> {
  final _formKey = GlobalKey<FormState>();
  final _employeeNumber = TextEditingController();
  final _firstName = TextEditingController();
  final _lastName = TextEditingController();
  final _workEmail = TextEditingController();
  final _phone = TextEditingController();

  DateTime _joinDate = DateUtils.dateOnly(DateTime.now());
  String? _departmentId;
  String? _legalEntityId;
  String? _designationId;
  String? _locationId;
  String? _managerId;
  String _employmentType = 'FullTime';
  bool _createLoginAccount = true;
  String? _error;
  bool _saving = false;

  @override
  void dispose() {
    _employeeNumber.dispose();
    _firstName.dispose();
    _lastName.dispose();
    _workEmail.dispose();
    _phone.dispose();
    super.dispose();
  }

  String _dateText(DateTime date) =>
      '${date.year.toString().padLeft(4, '0')}-'
      '${date.month.toString().padLeft(2, '0')}-'
      '${date.day.toString().padLeft(2, '0')}';

  Future<void> _pickJoinDate() async {
    final picked = await showDatePicker(
      context: context,
      initialDate: _joinDate,
      firstDate: DateTime(1900),
      lastDate: DateTime(2100),
      helpText: Strings.joiningDate,
    );
    if (picked != null) setState(() => _joinDate = DateUtils.dateOnly(picked));
  }

  Future<void> _submit() async {
    FocusScope.of(context).unfocus();
    if (!(_formKey.currentState?.validate() ?? false)) return;

    setState(() {
      _saving = true;
      _error = null;
    });

    try {
      final api = ref.read(apiClientProvider);
      final employee = await api.postMap(
        '/employees',
        data: {
          if (_employeeNumber.text.trim().isNotEmpty)
            'employeeNumber': _employeeNumber.text.trim(),
          'firstName': _firstName.text.trim(),
          'lastName': _lastName.text.trim(),
          'workEmail': _workEmail.text.trim(),
          if (_phone.text.trim().isNotEmpty) 'phone': _phone.text.trim(),
          'joinDate': _dateText(_joinDate),
          'designationId': _designationId,
          'departmentId': _departmentId,
          'locationId': _locationId,
          'legalEntityId': _legalEntityId,
          if (_managerId != null) 'managerId': _managerId,
          'employmentType': _employmentType,
        },
      );
      if (!mounted) return;

      final employeeId =
          employee['employeeId']?.toString() ?? employee['id']?.toString();
      if (_createLoginAccount && employeeId != null && employeeId.isNotEmpty) {
        try {
          final account = await api.postMap(
            '/admin/users',
            data: {
              'employeeId': employeeId,
              'roles': ['Employee'],
            },
          );
          if (!mounted) return;
          await _showTemporaryCredentials(
            account['temporaryPassword']?.toString(),
          );
        } on ApiException catch (error) {
          if (!mounted) return;
          await showDialog<void>(
            context: context,
            builder: (context) => AlertDialog(
              title: const Text('Employee added, account not created'),
              content: Text(
                'The employee record was saved, but a login account could not be created: ${error.message}',
              ),
              actions: [
                TextButton(
                  onPressed: () => Navigator.of(context).pop(),
                  child: const Text(Strings.actionCompleted),
                ),
              ],
            ),
          );
          if (mounted) context.pop(true);
          return;
        }
      }
      if (!mounted) return;
      context.pop(true);
    } on ApiException catch (error) {
      if (mounted) setState(() => _error = error.message);
    } catch (_) {
      if (mounted) setState(() => _error = Strings.offlineMessage);
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  Future<void> _showTemporaryCredentials(String? password) async {
    if (password == null || password.isEmpty) {
      setState(
        () => _error = 'Employee was added, but the API did not return a temporary password.',
      );
      return;
    }

    final loginId = _workEmail.text.trim();
    await showDialog<void>(
      context: context,
      barrierDismissible: false,
      builder: (dialogContext) => AlertDialog(
        title: const Text('Employee login created'),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text('Login ID'),
            SelectableText(loginId),
            const SizedBox(height: 12),
            const Text('Temporary password'),
            SelectableText(password),
            const SizedBox(height: 12),
            const Text(
              'Share this password securely. The employee will be asked to change it after signing in.',
            ),
          ],
        ),
        actions: [
          TextButton.icon(
            onPressed: () async {
              await Clipboard.setData(ClipboardData(text: password));
              if (dialogContext.mounted) {
                ScaffoldMessenger.of(dialogContext).showSnackBar(
                  const SnackBar(content: Text('Temporary password copied')),
                );
              }
            },
            icon: const Icon(Icons.copy_rounded),
            label: const Text('Copy password'),
          ),
          FilledButton(
            onPressed: () => Navigator.of(dialogContext).pop(),
            child: const Text(Strings.actionCompleted),
          ),
        ],
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final options = ref.watch(employeeCreateOptionsProvider);
    return AppScaffold(
      title: Strings.addEmployee,
      body: SafeArea(
        child: options.when(
          loading: () => const Center(child: CircularProgressIndicator()),
          error: (error, stack) => Center(
            child: Padding(
              padding: const EdgeInsets.all(24),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Text(error.toString(), textAlign: TextAlign.center),
                  const SizedBox(height: 12),
                  TextButton(
                    onPressed: () =>
                        ref.invalidate(employeeCreateOptionsProvider),
                    child: const Text('Retry'),
                  ),
                ],
              ),
            ),
          ),
          data: _buildForm,
        ),
      ),
    );
  }

  Widget _buildForm(EmployeeCreateOptions options) {
    final theme = Theme.of(context);
    return Form(
      key: _formKey,
      child: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          Text(
            'Enter the new employee’s work details. The employee number can be generated automatically.',
            style: theme.textTheme.bodyMedium?.copyWith(
              color: theme.colorScheme.onSurfaceVariant,
            ),
          ),
          const SizedBox(height: 18),
          if (_error != null) ...[
            Container(
              padding: const EdgeInsets.all(12),
              decoration: BoxDecoration(
                color: theme.colorScheme.errorContainer,
                borderRadius: BorderRadius.circular(12),
              ),
              child: Text(
                _error!,
                style: TextStyle(color: theme.colorScheme.onErrorContainer),
              ),
            ),
            const SizedBox(height: 16),
          ],
          TextFormField(
            controller: _employeeNumber,
            enabled: !_saving,
            textCapitalization: TextCapitalization.characters,
            decoration: const InputDecoration(
              labelText: Strings.employeeNumberOptional,
              prefixIcon: Icon(Icons.badge_outlined),
            ),
          ),
          const SizedBox(height: 14),
          TextFormField(
            controller: _firstName,
            enabled: !_saving,
            textCapitalization: TextCapitalization.words,
            validator: (value) => Validators.required(value),
            decoration: const InputDecoration(
              labelText: Strings.firstName,
              prefixIcon: Icon(Icons.person_outline_rounded),
            ),
          ),
          const SizedBox(height: 14),
          TextFormField(
            controller: _lastName,
            enabled: !_saving,
            textCapitalization: TextCapitalization.words,
            validator: (value) => Validators.required(value),
            decoration: const InputDecoration(
              labelText: Strings.lastName,
              prefixIcon: Icon(Icons.person_outline_rounded),
            ),
          ),
          const SizedBox(height: 14),
          TextFormField(
            controller: _workEmail,
            enabled: !_saving,
            keyboardType: TextInputType.emailAddress,
            autofillHints: const [AutofillHints.email],
            validator: Validators.email,
            decoration: const InputDecoration(
              labelText: Strings.workEmail,
              prefixIcon: Icon(Icons.email_outlined),
            ),
          ),
          const SizedBox(height: 14),
          TextFormField(
            controller: _phone,
            enabled: !_saving,
            keyboardType: TextInputType.phone,
            validator: Validators.phoneIfPresent,
            decoration: const InputDecoration(
              labelText: Strings.phone,
              prefixIcon: Icon(Icons.phone_outlined),
            ),
          ),
          const SizedBox(height: 14),
          TextFormField(
            key: ValueKey(_dateText(_joinDate)),
            initialValue: _dateText(_joinDate),
            readOnly: true,
            onTap: _saving ? null : _pickJoinDate,
            decoration: const InputDecoration(
              labelText: Strings.joiningDate,
              prefixIcon: Icon(Icons.calendar_month_outlined),
            ),
          ),
          const SizedBox(height: 14),
          DropdownButtonFormField<String>(
            initialValue: _employmentType,
            decoration: const InputDecoration(
              labelText: Strings.employmentType,
            ),
            items: const [
              DropdownMenuItem(value: 'FullTime', child: Text('Full time')),
              DropdownMenuItem(value: 'PartTime', child: Text('Part time')),
              DropdownMenuItem(value: 'Contract', child: Text('Contract')),
              DropdownMenuItem(value: 'Intern', child: Text('Intern')),
            ],
            onChanged: _saving
                ? null
                : (value) {
                    if (value != null) setState(() => _employmentType = value);
                  },
          ),
          const SizedBox(height: 14),
          Builder(
            builder: (context) {
              // Fall back to all org units if none are explicitly typed as a
              // legal entity (some orgs only have departments configured).
              final entityOptions = options.legalEntities.isNotEmpty
                  ? options.legalEntities
                  : options.orgUnits;
              return DropdownButtonFormField<String>(
                initialValue: _legalEntityId,
                isExpanded: true,
                decoration: const InputDecoration(
                  labelText: Strings.legalEntity,
                ),
                validator: (value) =>
                    value == null ? Strings.selectLegalEntity : null,
                items: [
                  for (final entity in entityOptions)
                    DropdownMenuItem(
                      value: entity.id,
                      child: Text(
                        entity.name,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                      ),
                    ),
                ],
                onChanged: _saving
                    ? null
                    : (value) => setState(() => _legalEntityId = value),
              );
            },
          ),
          const SizedBox(height: 14),
          DropdownButtonFormField<String?>(
            initialValue: _managerId,
            decoration: const InputDecoration(
              labelText: Strings.reportingManager,
            ),
            items: [
              const DropdownMenuItem<String?>(
                value: null,
                child: Text('No reporting manager'),
              ),
              for (final manager in options.managers)
                DropdownMenuItem<String?>(
                  value: manager.employeeId,
                  child: Text(
                    manager.label,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                  ),
                ),
            ],
            onChanged: _saving
                ? null
                : (value) => setState(() => _managerId = value),
          ),
          const SizedBox(height: 14),
          DropdownButtonFormField<String>(
            initialValue: _departmentId,
            decoration: const InputDecoration(labelText: Strings.department),
            validator: (value) =>
                value == null ? Strings.selectDepartment : null,
            items: [
              for (final department in options.departments)
                DropdownMenuItem(
                  value: department.id,
                  child: Text(department.name),
                ),
            ],
            onChanged: _saving
                ? null
                : (value) => setState(() => _departmentId = value),
          ),
          const SizedBox(height: 14),
          DropdownButtonFormField<String>(
            initialValue: _designationId,
            decoration: const InputDecoration(labelText: Strings.designation),
            validator: (value) =>
                value == null ? Strings.selectDesignation : null,
            items: [
              for (final designation in options.designations)
                DropdownMenuItem(
                  value: designation.id,
                  child: Text(designation.name),
                ),
            ],
            onChanged: _saving
                ? null
                : (value) => setState(() => _designationId = value),
          ),
          const SizedBox(height: 14),
          DropdownButtonFormField<String>(
            initialValue: _locationId,
            decoration: const InputDecoration(labelText: Strings.location),
            validator: (value) => value == null ? Strings.selectLocation : null,
            items: [
              for (final location in options.locations)
                DropdownMenuItem(
                  value: location.id,
                  child: Text(
                    location.city == null || location.city!.isEmpty
                        ? location.name
                        : '${location.name} · ${location.city}',
                  ),
                ),
            ],
            onChanged: _saving
                ? null
                : (value) => setState(() => _locationId = value),
          ),
          const SizedBox(height: 8),
          CheckboxListTile(
            contentPadding: EdgeInsets.zero,
            value: _createLoginAccount,
            onChanged: _saving
                ? null
                : (value) =>
                      setState(() => _createLoginAccount = value ?? false),
            title: const Text('Create login account'),
            subtitle: const Text(
              'Generate an employee login ID and one-time temporary password.',
            ),
          ),
          const SizedBox(height: 24),
          AppButton(
            label: _saving ? Strings.saving : Strings.createEmployee,
            busy: _saving,
            onPressed: _saving ? null : _submit,
          ),
          const SizedBox(height: 24),
        ],
      ),
    );
  }
}
