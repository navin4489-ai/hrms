import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/network/api_exception.dart';
import '../../core/strings.dart';
import '../../providers/settings_provider.dart';
import '../../widgets/app_button.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/form_error.dart';

/// `POST /auth/forgot-password` (FR-EMP-002). Always answers 202 so the UI
/// never reveals whether an account exists.
class ForgotPasswordScreen extends ConsumerStatefulWidget {
  const ForgotPasswordScreen({super.key});

  @override
  ConsumerState<ForgotPasswordScreen> createState() =>
      _ForgotPasswordScreenState();
}

class _ForgotPasswordScreenState extends ConsumerState<ForgotPasswordScreen>
    with ServerFieldErrors<ForgotPasswordScreen> {
  final _formKey = GlobalKey<FormState>();
  final _usernameController = TextEditingController();
  bool _busy = false;
  bool _sent = false;

  @override
  void dispose() {
    _usernameController.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    FocusScope.of(context).unfocus();
    clearErrors();
    if (!(_formKey.currentState?.validate() ?? false)) return;
    setState(() => _busy = true);
    try {
      await ref
          .read(settingsActionsProvider)
          .forgotPassword(_usernameController.text);
      if (mounted) setState(() => _sent = true);
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
    final scheme = theme.colorScheme;

    return AppScaffold(
      title: Strings.resetPassword,
      showBell: false,
      body: SafeArea(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(24),
          child: _sent
              ? _SentPanel(onBack: () => context.go('/login'))
              : Form(
                  key: _formKey,
                  autovalidateMode: AutovalidateMode.onUserInteraction,
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      const SizedBox(height: 16),
                      Icon(
                        Icons.lock_reset_rounded,
                        size: 56,
                        color: scheme.primary,
                      ),
                      const SizedBox(height: 16),
                      Text(
                        Strings.resetPassword,
                        style: theme.textTheme.titleLarge?.copyWith(
                          fontWeight: FontWeight.w700,
                        ),
                        textAlign: TextAlign.center,
                      ),
                      const SizedBox(height: 8),
                      Text(
                        'Enter your username, work email or employee number '
                        'and we will send you a reset link.',
                        style: theme.textTheme.bodyMedium?.copyWith(
                          color: scheme.onSurfaceVariant,
                        ),
                        textAlign: TextAlign.center,
                      ),
                      const SizedBox(height: 24),
                      if (bannerMessage != null) ...[
                        FormErrorBanner(
                          message: bannerMessage!,
                          onDismiss: clearErrors,
                        ),
                      ],
                      TextFormField(
                        controller: _usernameController,
                        enabled: !_busy,
                        textInputAction: TextInputAction.done,
                        autofillHints: const [AutofillHints.username],
                        onFieldSubmitted: (_) => _submit(),
                        validator: (v) => (v == null || v.trim().isEmpty)
                            ? Strings.fieldRequired
                            : null,
                        decoration: InputDecoration(
                          labelText: Strings.usernameLabel,
                          hintText: Strings.username,
                          prefixIcon: const Icon(Icons.person_outline_rounded),
                          errorText: errorFor('username'),
                        ),
                      ),
                      const SizedBox(height: 20),
                      AppButton(
                        label: Strings.sendResetLink,
                        busy: _busy,
                        onPressed: _busy ? null : _submit,
                      ),
                      const SizedBox(height: 12),
                      TextButton(
                        onPressed: _busy ? null : () => context.go('/login'),
                        child: const Text('Back to sign in'),
                      ),
                    ],
                  ),
                ),
        ),
      ),
    );
  }
}

class _SentPanel extends StatelessWidget {
  const _SentPanel({required this.onBack});

  final VoidCallback onBack;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SizedBox(height: 24),
        Container(
          padding: const EdgeInsets.all(20),
          decoration: BoxDecoration(
            color: scheme.primaryContainer,
            shape: BoxShape.circle,
          ),
          child: Icon(
            Icons.mark_email_read_rounded,
            size: 44,
            color: scheme.onPrimaryContainer,
          ),
        ),
        const SizedBox(height: 20),
        Text(
          'Check your inbox',
          style: theme.textTheme.titleLarge?.copyWith(
            fontWeight: FontWeight.w700,
          ),
          textAlign: TextAlign.center,
        ),
        const SizedBox(height: 10),
        Text(
          Strings.resetSent,
          style: theme.textTheme.bodyMedium?.copyWith(
            color: scheme.onSurfaceVariant,
          ),
          textAlign: TextAlign.center,
        ),
        const SizedBox(height: 8),
        Text(
          Strings.resetLinkDevNote,
          style: theme.textTheme.bodySmall?.copyWith(
            color: scheme.onSurfaceVariant,
          ),
          textAlign: TextAlign.center,
        ),
        const SizedBox(height: 28),
        AppButton(label: 'Back to sign in', onPressed: onBack),
      ],
    );
  }
}
