import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/network/api_exception.dart';
import '../../core/strings.dart';
import '../../core/utils/validators.dart';
import '../../providers/announcements_provider.dart';
import '../../providers/auth_provider.dart';
import '../../providers/directory_provider.dart';
import '../../widgets/app_button.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';

class CreateAnnouncementScreen extends ConsumerStatefulWidget {
  const CreateAnnouncementScreen({super.key});

  @override
  ConsumerState<CreateAnnouncementScreen> createState() =>
      _CreateAnnouncementScreenState();
}

class _CreateAnnouncementScreenState
    extends ConsumerState<CreateAnnouncementScreen> {
  final _formKey = GlobalKey<FormState>();
  final _title = TextEditingController();
  final _summary = TextEditingController();
  final _body = TextEditingController();
  final _publishAt = TextEditingController();
  final _expiresAt = TextEditingController();
  String _category = 'Company';
  String _audience = 'All';
  String? _audienceId;
  bool _requiresAcknowledgement = false;
  bool _pinned = false;
  bool _busy = false;
  String? _error;
  Map<String, String> _fieldErrors = {};

  static const _categories = [
    'Company',
    'HR',
    'Policy',
    'Event',
    'IT',
    'Wellness',
  ];
  static const _audiences = ['All', 'Department', 'Location'];

  @override
  void dispose() {
    _title.dispose();
    _summary.dispose();
    _body.dispose();
    _publishAt.dispose();
    _expiresAt.dispose();
    super.dispose();
  }

  Future<DateTime?> _pickDate(DateTime? initial) => showDatePicker(
    context: context,
    initialDate: initial ?? DateTime.now(),
    firstDate: DateTime.now().subtract(const Duration(days: 365)),
    lastDate: DateTime.now().add(const Duration(days: 3650)),
  );

  Future<void> _selectDate(TextEditingController controller) async {
    final picked = await _pickDate(DateTime.tryParse(controller.text));
    if (picked != null) {
      setState(() {
        controller.text = _date(picked);
        final key = controller == _publishAt ? 'publishAt' : 'expiresAt';
        _removeFieldError(key);
        _error = null;
      });
    }
  }

  String? _fieldError(String key) {
    for (final entry in _fieldErrors.entries) {
      if (entry.key.toLowerCase() == key.toLowerCase()) return entry.value;
    }
    return null;
  }

  void _clearFieldError(String key) {
    if (_fieldErrors.keys.any(
      (field) => field.toLowerCase() == key.toLowerCase(),
    )) {
      setState(() {
        _removeFieldError(key);
        _error = null;
      });
    }
  }

  void _removeFieldError(String key) => _fieldErrors.removeWhere(
    (field, _) => field.toLowerCase() == key.toLowerCase(),
  );

  String _date(DateTime date) =>
      '${date.year.toString().padLeft(4, '0')}-'
      '${date.month.toString().padLeft(2, '0')}-'
      '${date.day.toString().padLeft(2, '0')}T09:00:00';

  Future<void> _submit() async {
    setState(() {
      _fieldErrors = {};
      _error = null;
    });
    if (!(_formKey.currentState?.validate() ?? false)) return;
    if (_audience != 'All' && _audienceId == null) {
      setState(() {
        _fieldErrors = {
          'audienceIds': 'Select the target department or location.',
        };
      });
      return;
    }
    final publishAt = DateTime.tryParse(_publishAt.text);
    final expiresAt = DateTime.tryParse(_expiresAt.text);
    if (publishAt != null &&
        expiresAt != null &&
        !expiresAt.isAfter(publishAt)) {
      setState(() {
        _fieldErrors = {'expiresAt': 'Expiry must be after the publish date.'};
      });
      return;
    }

    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final actions = ref.read(announcementActionsProvider);
      final request = <String, dynamic>{
        'title': _title.text.trim(),
        'summary': _summary.text.trim(),
        'body': _body.text.trim(),
        'category': _category,
        'audience': _audience,
        if (_audienceId != null) 'audienceIds': [_audienceId],
        'requiresAcknowledgement': _requiresAcknowledgement,
        'pinned': _pinned,
        if (_publishAt.text.isNotEmpty) 'publishAt': _publishAt.text,
        if (_expiresAt.text.isNotEmpty) 'expiresAt': _expiresAt.text,
      };
      final draft = await actions.create(request);
      final id = draft['id']?.toString();
      if (_publishAt.text.isEmpty && id != null) {
        try {
          await actions.publish(id);
        } on ApiException catch (error) {
          if (mounted) {
            ScaffoldMessenger.of(context).showSnackBar(
              SnackBar(
                content: Text('Draft saved; publish failed: ${error.message}'),
              ),
            );
          }
        }
      }
      ref.invalidate(announcementsProvider);
      if (mounted) context.pop(true);
    } on ApiException catch (error) {
      if (mounted) {
        setState(() {
          _fieldErrors = {
            for (final entry in error.errors.entries)
              if (entry.value.isNotEmpty) entry.key: entry.value.first,
          };
          _error = error.errors.isEmpty ? error.message : error.detail;
        });
      }
    } catch (_) {
      if (mounted) setState(() => _error = Strings.offlineMessage);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final auth = ref.watch(authStateProvider);
    if (!auth.hasPermission('announcement.create')) {
      return const AppScaffold(
        title: 'Create announcement',
        body: Center(
          child: Text('You do not have permission to publish announcements.'),
        ),
      );
    }

    final departments = ref.watch(departmentsProvider);
    final locations = ref.watch(locationsProvider);
    return AppScaffold(
      title: 'New announcement',
      body: AsyncBody(
        value: departments,
        onRetry: () => ref.invalidate(departmentsProvider),
        builder: (departmentItems) => AsyncBody(
          value: locations,
          onRetry: () => ref.invalidate(locationsProvider),
          builder: (locationItems) => Form(
            key: _formKey,
            child: ListView(
              padding: const EdgeInsets.all(16),
              children: [
                if (_error != null) ...[
                  Text(
                    _error!,
                    style: TextStyle(
                      color: Theme.of(context).colorScheme.error,
                    ),
                  ),
                  const SizedBox(height: 12),
                ],
                TextFormField(
                  controller: _title,
                  enabled: !_busy,
                  maxLength: 200,
                  validator: (value) {
                    final required = Validators.required(value);
                    if (required != null) return required;
                    return value!.trim().length > 200
                        ? 'Title must be 200 characters or fewer.'
                        : null;
                  },
                  onChanged: (_) => _clearFieldError('title'),
                  decoration: InputDecoration(
                    labelText: 'Title',
                    errorText: _fieldError('title'),
                  ),
                ),
                const SizedBox(height: 12),
                TextFormField(
                  controller: _summary,
                  enabled: !_busy,
                  maxLength: 500,
                  validator: (value) {
                    final required = Validators.required(value);
                    if (required != null) return required;
                    return value!.trim().length > 500
                        ? 'Summary must be 500 characters or fewer.'
                        : null;
                  },
                  onChanged: (_) => _clearFieldError('summary'),
                  decoration: InputDecoration(
                    labelText: 'Summary',
                    errorText: _fieldError('summary'),
                  ),
                ),
                const SizedBox(height: 12),
                TextFormField(
                  controller: _body,
                  enabled: !_busy,
                  minLines: 4,
                  maxLines: 8,
                  maxLength: 8000,
                  validator: (value) {
                    final required = Validators.required(value);
                    if (required != null) return required;
                    return value!.trim().length > 8000
                        ? 'Announcement must be 8000 characters or fewer.'
                        : null;
                  },
                  onChanged: (_) => _clearFieldError('body'),
                  decoration: InputDecoration(
                    labelText: 'Announcement',
                    errorText: _fieldError('body'),
                  ),
                ),
                const SizedBox(height: 12),
                DropdownButtonFormField<String>(
                  initialValue: _category,
                  decoration: InputDecoration(
                    labelText: 'Category',
                    errorText: _fieldError('category'),
                  ),
                  items: [
                    for (final item in _categories)
                      DropdownMenuItem(value: item, child: Text(item)),
                  ],
                  onChanged: _busy
                      ? null
                      : (value) => setState(() {
                          _category = value ?? _category;
                          _removeFieldError('category');
                          _error = null;
                        }),
                ),
                const SizedBox(height: 12),
                DropdownButtonFormField<String>(
                  initialValue: _audience,
                  decoration: InputDecoration(
                    labelText: 'Audience',
                    errorText: _fieldError('audience'),
                  ),
                  items: [
                    for (final item in _audiences)
                      DropdownMenuItem(value: item, child: Text(item)),
                  ],
                  onChanged: _busy
                      ? null
                      : (value) => setState(() {
                          _audience = value ?? 'All';
                          _audienceId = null;
                          _removeFieldError('audience');
                          _removeFieldError('audienceIds');
                          _error = null;
                        }),
                ),
                const SizedBox(height: 12),
                TextFormField(
                  controller: _publishAt,
                  enabled: !_busy,
                  readOnly: true,
                  onTap: _busy ? null : () => _selectDate(_publishAt),
                  decoration: InputDecoration(
                    labelText: 'Schedule publish (optional)',
                    hintText: 'Leave blank to publish now',
                    errorText: _fieldError('publishAt'),
                    prefixIcon: const Icon(Icons.schedule_rounded),
                    suffixIcon: _publishAt.text.isEmpty
                        ? null
                        : IconButton(
                            tooltip: Strings.close,
                            onPressed: _busy
                                ? null
                                : () => setState(() {
                                    _publishAt.clear();
                                    _removeFieldError('publishAt');
                                    _error = null;
                                  }),
                            icon: const Icon(Icons.close_rounded),
                          ),
                  ),
                ),
                const SizedBox(height: 12),
                TextFormField(
                  controller: _expiresAt,
                  enabled: !_busy,
                  readOnly: true,
                  onTap: _busy ? null : () => _selectDate(_expiresAt),
                  decoration: InputDecoration(
                    labelText: 'Expiry date (optional)',
                    errorText: _fieldError('expiresAt'),
                    prefixIcon: const Icon(Icons.event_busy_outlined),
                    suffixIcon: _expiresAt.text.isEmpty
                        ? null
                        : IconButton(
                            tooltip: Strings.close,
                            onPressed: _busy
                                ? null
                                : () => setState(() {
                                    _expiresAt.clear();
                                    _removeFieldError('expiresAt');
                                    _error = null;
                                  }),
                            icon: const Icon(Icons.close_rounded),
                          ),
                  ),
                ),
                if (_audience == 'Department') ...[
                  const SizedBox(height: 12),
                  DropdownButtonFormField<String>(
                    initialValue: _audienceId,
                    decoration: InputDecoration(
                      labelText: Strings.department,
                      errorText: _fieldError('audienceIds'),
                    ),
                    items: [
                      for (final item in departmentItems)
                        DropdownMenuItem(
                          value: item.id,
                          child: Text(item.name),
                        ),
                    ],
                    onChanged: _busy
                        ? null
                        : (value) => setState(() {
                            _audienceId = value;
                            _removeFieldError('audienceIds');
                            _error = null;
                          }),
                  ),
                ],
                if (_audience == 'Location') ...[
                  const SizedBox(height: 12),
                  DropdownButtonFormField<String>(
                    initialValue: _audienceId,
                    decoration: InputDecoration(
                      labelText: Strings.location,
                      errorText: _fieldError('audienceIds'),
                    ),
                    items: [
                      for (final item in locationItems)
                        DropdownMenuItem(
                          value: item.id,
                          child: Text(item.name),
                        ),
                    ],
                    onChanged: _busy
                        ? null
                        : (value) => setState(() {
                            _audienceId = value;
                            _removeFieldError('audienceIds');
                            _error = null;
                          }),
                  ),
                ],
                SwitchListTile(
                  contentPadding: EdgeInsets.zero,
                  title: const Text('Require acknowledgement'),
                  value: _requiresAcknowledgement,
                  onChanged: _busy
                      ? null
                      : (value) =>
                            setState(() => _requiresAcknowledgement = value),
                ),
                SwitchListTile(
                  contentPadding: EdgeInsets.zero,
                  title: const Text('Pin announcement'),
                  value: _pinned,
                  onChanged: _busy
                      ? null
                      : (value) => setState(() => _pinned = value),
                ),
                const SizedBox(height: 18),
                AppButton(
                  label: _busy ? Strings.saving : 'Publish now',
                  busy: _busy,
                  onPressed: _busy ? null : _submit,
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
