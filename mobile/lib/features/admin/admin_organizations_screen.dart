import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/network/api_exception.dart';
import '../../core/strings.dart';
import '../../providers/organizations_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';
import '../../widgets/status_chip.dart';

/// SuperAdmin multi-tenant organization management (FR-ADM-001/002): list, create and
/// configure organizations, toggle per-org modules and view employee-code settings.
class AdminOrganizationsScreen extends ConsumerWidget {
  const AdminOrganizationsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final orgs = ref.watch(organizationsProvider);
    return AppScaffold(
      title: 'Organizations',
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => _create(context, ref),
        icon: const Icon(Icons.add),
        label: const Text('New organization'),
      ),
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(organizationsProvider),
        child: AsyncBody(
          value: orgs,
          onRetry: () => ref.invalidate(organizationsProvider),
          isEmpty: (items) => items.isEmpty,
          emptyTitle: 'No organizations',
          emptyMessage: 'Create your first organization to onboard employees.',
          emptyIcon: Icons.apartment_outlined,
          builder: (items) => ListView.separated(
            physics: const AlwaysScrollableScrollPhysics(),
            padding: const EdgeInsets.fromLTRB(16, 16, 16, 90),
            itemCount: items.length,
            separatorBuilder: (_, _) => const SizedBox(height: 8),
            itemBuilder: (context, index) {
              final org = items[index];
              return Card(
                child: ListTile(
                  leading: CircleAvatar(
                    child: Text(
                      org.code.isNotEmpty ? org.code[0] : '?',
                      style: const TextStyle(fontWeight: FontWeight.bold),
                    ),
                  ),
                  title: Text('${org.name} · ${org.code}'),
                  subtitle: Text(
                    '${org.employeeCount} employee(s) · ${org.country} · ${org.currency}',
                  ),
                  trailing: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      StatusChip(status: org.status, dense: true),
                      IconButton(
                        tooltip: 'Modules & settings',
                        onPressed: () => _openDetail(context, ref, org.id),
                        icon: const Icon(Icons.tune_rounded),
                      ),
                    ],
                  ),
                  onTap: () => _openDetail(context, ref, org.id),
                ),
              );
            },
          ),
        ),
      ),
    );
  }

  Future<void> _create(BuildContext context, WidgetRef ref) async {
    final name = TextEditingController();
    final code = TextEditingController();
    final country = TextEditingController(text: 'IN');
    final timeZone = TextEditingController(text: 'Asia/Kolkata');
    final currency = TextEditingController(text: 'INR');
    final prefix = TextEditingController();
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
              await ref
                  .read(organizationActionsProvider)
                  .create(
                    name: name.text.trim(),
                    code: code.text.trim().toUpperCase(),
                    country: country.text.trim().toUpperCase(),
                    timeZone: timeZone.text.trim(),
                    currency: currency.text.trim().toUpperCase(),
                    employeeCodePrefix: prefix.text.trim().isEmpty
                        ? null
                        : prefix.text.trim(),
                  );
              if (dialogContext.mounted) Navigator.pop(dialogContext, true);
            } on ApiException catch (error) {
              setDialogState(() {
                fieldErrors = _fieldErrors(error);
                formError = error.errors.isEmpty ? error.message : error.detail;
                saving = false;
              });
            } catch (error) {
              setDialogState(() {
                formError = 'Could not create organization: $error';
                saving = false;
              });
            }
          }

          return AlertDialog(
            title: const Text('New organization'),
            content: SizedBox(
              width: 500,
              child: Form(
                key: formKey,
                child: SingleChildScrollView(
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      if (formError != null)
                        Padding(
                          padding: const EdgeInsets.only(bottom: 10),
                          child: Align(
                            alignment: Alignment.centerLeft,
                            child: Text(
                              formError!,
                              style: const TextStyle(color: Colors.red),
                            ),
                          ),
                        ),
                      _field(name, 'Organization name', fieldErrors['name']),
                      _field(
                        code,
                        'Organization code (e.g. ACME)',
                        fieldErrors['code'],
                      ),
                      Row(
                        children: [
                          Expanded(
                            child: _field(
                              country,
                              'Country',
                              fieldErrors['country'],
                            ),
                          ),
                          const SizedBox(width: 10),
                          Expanded(
                            child: _field(
                              currency,
                              'Currency',
                              fieldErrors['currency'],
                              required: false,
                            ),
                          ),
                        ],
                      ),
                      _field(
                        timeZone,
                        'Time zone',
                        fieldErrors['timeZone'],
                        required: false,
                      ),
                      _field(
                        prefix,
                        'Employee code prefix (optional, defaults to CODE-EMP)',
                        fieldErrors['employeeCodePrefix'],
                        required: false,
                      ),
                    ],
                  ),
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

    for (final c in [name, code, country, timeZone, currency, prefix]) {
      c.dispose();
    }
    if (saved == true && context.mounted) {
      ref.invalidate(organizationsProvider);
      ScaffoldMessenger.of(context)
          .showSnackBar(const SnackBar(content: Text('Organization created')));
    }
  }

  Future<void> _openDetail(
    BuildContext context,
    WidgetRef ref,
    String orgId,
  ) async {
    await showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => _OrgDetailSheet(orgId: orgId),
    );
    ref.invalidate(organizationsProvider);
  }
}

class _OrgDetailSheet extends ConsumerStatefulWidget {
  const _OrgDetailSheet({required this.orgId});

  final String orgId;

  @override
  ConsumerState<_OrgDetailSheet> createState() => _OrgDetailSheetState();
}

class _OrgDetailSheetState extends ConsumerState<_OrgDetailSheet> {
  final Map<String, bool> _pending = {};
  bool _busy = false;

  void _snack(String message) {
    if (mounted) {
      ScaffoldMessenger.of(context)
          .showSnackBar(SnackBar(content: Text(message)));
    }
  }

  Future<void> _saveModules() async {
    if (_pending.isEmpty) return;
    setState(() => _busy = true);
    try {
      await ref
          .read(organizationActionsProvider)
          .setModules(id: widget.orgId, modules: Map.of(_pending));
      ref.invalidate(organizationDetailProvider(widget.orgId));
      _pending.clear();
      _snack('Modules updated');
    } on ApiException catch (e) {
      _snack(e.detail ?? e.message);
    } catch (_) {
      _snack(Strings.offlineMessage);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final detail = ref.watch(organizationDetailProvider(widget.orgId));
    return SafeArea(
      child: SizedBox(
        height: MediaQuery.sizeOf(context).height * .85,
        child: AsyncBody(
          value: detail,
          onRetry: () =>
              ref.invalidate(organizationDetailProvider(widget.orgId)),
          builder: (d) => Column(
            children: [
              Expanded(
                child: ListView(
                  padding: const EdgeInsets.all(16),
                  children: [
                    Text(
                      '${d.organization.name} · ${d.organization.code}',
                      style: Theme.of(context).textTheme.titleLarge,
                    ),
                    const SizedBox(height: 4),
                    Text(
                      '${d.organization.employeeCount} employee(s) · '
                      '${d.organization.country} · ${d.organization.currency} · '
                      '${d.organization.timeZone}',
                      style: Theme.of(context).textTheme.bodySmall,
                    ),
                    const SizedBox(height: 4),
                    Text(
                      'Employee codes: ${d.employeeCodePrefix}'
                      '${'0'.padLeft(d.employeeCodePadding, '0')} '
                      '(next #${d.employeeCodeNext})',
                      style: Theme.of(context).textTheme.bodySmall,
                    ),
                    const Divider(height: 28),
                    Text(
                      'Modules enabled for this organization',
                      style: Theme.of(context).textTheme.titleMedium,
                    ),
                    const SizedBox(height: 4),
                    Text(
                      'Disabled modules are hidden from this organization\u2019s '
                      'employees and admins.',
                      style: Theme.of(context).textTheme.bodySmall,
                    ),
                    const SizedBox(height: 8),
                    for (final m in d.modules)
                      SwitchListTile(
                        contentPadding: EdgeInsets.zero,
                        title: Text(m.label),
                        subtitle: Text(m.key),
                        value: _pending[m.key] ?? m.enabled,
                        onChanged: _busy
                            ? null
                            : (v) => setState(() => _pending[m.key] = v),
                      ),
                  ],
                ),
              ),
              const Divider(height: 1),
              Padding(
                padding: const EdgeInsets.all(12),
                child: Row(
                  children: [
                    Expanded(
                      child: FilledButton.icon(
                        onPressed: _busy || _pending.isEmpty
                            ? null
                            : _saveModules,
                        icon: _busy
                            ? const SizedBox(
                                width: 18,
                                height: 18,
                                child: CircularProgressIndicator(
                                  strokeWidth: 2,
                                ),
                              )
                            : const Icon(Icons.save_outlined),
                        label: const Text('Save module changes'),
                      ),
                    ),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

Map<String, String> _fieldErrors(ApiException error) => {
  for (final entry in error.errors.entries)
    if (entry.value.isNotEmpty) entry.key: entry.value.first,
};

Widget _field(
  TextEditingController controller,
  String label,
  String? errorText, {
  bool required = true,
}) => Padding(
  padding: const EdgeInsets.only(bottom: 8),
  child: TextFormField(
    controller: controller,
    decoration: InputDecoration(labelText: label, errorText: errorText),
    validator: required
        ? (value) => value == null || value.trim().isEmpty
              ? Strings.fieldRequired
              : null
        : null,
  ),
);
