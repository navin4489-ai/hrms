import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/network/api_exception.dart';
import '../../core/strings.dart';
import '../../core/utils/validators.dart';
import '../../providers/profile_provider.dart';
import '../../widgets/app_button.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/form_error.dart';

/// Sensitive change request (FR-EMP-022): bank details / legal name /
/// national ID are verified by HR before they take effect (202 Accepted).
class SensitiveChangeScreen extends ConsumerStatefulWidget {
  const SensitiveChangeScreen({super.key});

  @override
  ConsumerState<SensitiveChangeScreen> createState() =>
      _SensitiveChangeScreenState();
}

class _SensitiveChangeScreenState extends ConsumerState<SensitiveChangeScreen>
    with ServerFieldErrors<SensitiveChangeScreen> {
  static const _fields = <String, String>{
    'BankAccount': 'Bank account details',
    'LegalName': 'Legal name',
    'NationalId': 'National ID',
  };

  final _formKey = GlobalKey<FormState>();
  final _valueController = TextEditingController();
  final _reasonController = TextEditingController();
  String _field = 'BankAccount';
  bool _busy = false;

  @override
  void dispose() {
    _valueController.dispose();
    _reasonController.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    FocusScope.of(context).unfocus();
    clearErrors();
    if (!(_formKey.currentState?.validate() ?? false)) return;
    setState(() => _busy = true);
    try {
      await ref
          .read(profileActionsProvider)
          .requestSensitiveChange(
            field: _field,
            requestedValue: _valueController.text.trim(),
            reason: _reasonController.text.trim(),
          );
      if (!mounted) return;
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(const SnackBar(content: Text(Strings.sensitiveSubmitted)));
      context.pop();
    } on ApiException catch (e) {
      captureError(e);
    } catch (_) {
      setState(() => bannerMessage = Strings.offlineMessage);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return AppScaffold(
      title: Strings.sensitiveChange,
      body: SafeArea(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(16),
          child: Form(
            key: _formKey,
            autovalidateMode: AutovalidateMode.onUserInteraction,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Container(
                  padding: const EdgeInsets.all(12),
                  decoration: BoxDecoration(
                    color: theme.colorScheme.tertiaryContainer,
                    borderRadius: BorderRadius.circular(12),
                  ),
                  child: Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Icon(
                        Icons.gpp_maybe_outlined,
                        size: 18,
                        color: theme.colorScheme.onTertiaryContainer,
                      ),
                      const SizedBox(width: 8),
                      Expanded(
                        child: Text(
                          Strings.sensitiveChangeHint,
                          style: TextStyle(
                            fontSize: 13,
                            color: theme.colorScheme.onTertiaryContainer,
                          ),
                        ),
                      ),
                    ],
                  ),
                ),
                const SizedBox(height: 16),
                if (bannerMessage != null)
                  FormErrorBanner(
                    message: bannerMessage!,
                    onDismiss: clearErrors,
                  ),
                DropdownButtonFormField<String>(
                  initialValue: _field,
                  decoration: InputDecoration(
                    labelText: Strings.sensitiveField,
                    prefixIcon: const Icon(Icons.admin_panel_settings_outlined),
                    errorText: errorFor('field'),
                  ),
                  items: [
                    for (final entry in _fields.entries)
                      DropdownMenuItem(
                        value: entry.key,
                        child: Text(entry.value),
                      ),
                  ],
                  onChanged: _busy
                      ? null
                      : (value) {
                          if (value != null) {
                            setState(() => _field = value);
                            clearErrors();
                          }
                        },
                ),
                const SizedBox(height: 14),
                TextFormField(
                  controller: _valueController,
                  enabled: !_busy,
                  validator: Validators.required,
                  decoration: InputDecoration(
                    labelText: Strings.requestedValue,
                    hintText: 'Enter the new value',
                    prefixIcon: const Icon(Icons.edit_note_rounded),
                    errorText: errorFor('requestedValue'),
                  ),
                ),
                const SizedBox(height: 14),
                TextFormField(
                  controller: _reasonController,
                  enabled: !_busy,
                  maxLines: 3,
                  maxLength: 300,
                  validator: Validators.reason,
                  decoration: InputDecoration(
                    labelText: Strings.changeReason,
                    hintText: 'Why do you need this change?',
                    errorText: errorFor('reason'),
                  ),
                ),
                const SizedBox(height: 20),
                AppButton(
                  label: Strings.submit,
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
