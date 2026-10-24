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

/// Change password (`POST /auth/change-password`): current + new + confirm.
class ChangePasswordScreen extends ConsumerStatefulWidget {
  const ChangePasswordScreen({super.key});

  @override
  ConsumerState<ChangePasswordScreen> createState() =>
      _ChangePasswordScreenState();
}

class _ChangePasswordScreenState extends ConsumerState<ChangePasswordScreen>
    with ServerFieldErrors<ChangePasswordScreen> {
  final _formKey = GlobalKey<FormState>();
  final _currentController = TextEditingController();
  final _newController = TextEditingController();
  final _confirmController = TextEditingController();
  bool _obscureNew = true;
  bool _busy = false;

  @override
  void dispose() {
    _currentController.dispose();
    _newController.dispose();
    _confirmController.dispose();
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
          .changePassword(
            currentPassword: _currentController.text,
            newPassword: _newController.text,
          );
      if (!mounted) return;
      ScaffoldMessenger.of(context)
          .showSnackBar(const SnackBar(content: Text('Password changed')));
      context.pop();
    } on ApiException catch (e) {
      // 400 -> wrong current password / policy violation; 422 -> field errors.
      captureError(e);
    } catch (_) {
      setState(() => bannerMessage = Strings.offlineMessage);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return AppScaffold(
      title: Strings.changePassword,
      body: SafeArea(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(16),
          child: Form(
            key: _formKey,
            autovalidateMode: AutovalidateMode.onUserInteraction,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                if (bannerMessage != null)
                  FormErrorBanner(
                    message: bannerMessage!,
                    onDismiss: clearErrors,
                  ),
                TextFormField(
                  controller: _currentController,
                  enabled: !_busy,
                  obscureText: true,
                  autofillHints: const [AutofillHints.password],
                  validator: Validators.required,
                  decoration: InputDecoration(
                    labelText: Strings.currentPassword,
                    prefixIcon: const Icon(Icons.lock_outline_rounded),
                    errorText: errorFor('currentPassword'),
                  ),
                ),
                const SizedBox(height: 14),
                TextFormField(
                  controller: _newController,
                  enabled: !_busy,
                  obscureText: _obscureNew,
                  autofillHints: const [AutofillHints.newPassword],
                  validator: Validators.newPassword,
                  decoration: InputDecoration(
                    labelText: Strings.newPassword,
                    prefixIcon: const Icon(Icons.password_rounded),
                    suffixIcon: IconButton(
                      tooltip: _obscureNew ? 'Show password' : 'Hide password',
                      onPressed: () =>
                          setState(() => _obscureNew = !_obscureNew),
                      icon: Icon(
                        _obscureNew
                            ? Icons.visibility_outlined
                            : Icons.visibility_off_outlined,
                      ),
                    ),
                    errorText: errorFor('newPassword'),
                  ),
                ),
                const SizedBox(height: 14),
                TextFormField(
                  controller: _confirmController,
                  enabled: !_busy,
                  obscureText: _obscureNew,
                  autofillHints: const [AutofillHints.newPassword],
                  validator: (value) =>
                      Validators.confirmPassword(value, _newController.text),
                  onFieldSubmitted: (_) => _submit(),
                  decoration: InputDecoration(
                    labelText: Strings.confirmPassword,
                    prefixIcon: const Icon(Icons.lock_reset_rounded),
                    errorText: errorFor('confirmPassword'),
                  ),
                ),
                const SizedBox(height: 24),
                AppButton(
                  label: Strings.changePassword,
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
