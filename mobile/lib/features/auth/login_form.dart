import 'package:flutter/material.dart';

import '../../core/strings.dart';
import '../../core/utils/validators.dart';
import '../../widgets/app_button.dart';

/// Credentials submitted by [LoginForm].
class LoginCredentials {
  const LoginCredentials({required this.username, required this.password});

  final String username;
  final String password;
}

/// Pure form widget for sign-in: client-side validation, deployment credential
/// guidance, and forgot-password link. The screen wires it to the auth controller.
class LoginForm extends StatefulWidget {
  const LoginForm({
    super.key,
    required this.onSubmit,
    this.busy = false,
    this.onForgotPassword,
    this.errorText,
  });

  /// Called with validated values; the caller performs the login and reports
  /// errors via [errorText].
  final Future<void> Function(LoginCredentials credentials) onSubmit;
  final bool busy;
  final VoidCallback? onForgotPassword;

  /// Blocking/credential error shown above the button.
  final String? errorText;

  @override
  State<LoginForm> createState() => _LoginFormState();
}

class _LoginFormState extends State<LoginForm> {
  final _formKey = GlobalKey<FormState>();
  final _usernameController = TextEditingController();
  final _passwordController = TextEditingController();
  bool _obscure = true;

  @override
  void dispose() {
    _usernameController.dispose();
    _passwordController.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    FocusScope.of(context).unfocus();
    final form = _formKey.currentState;
    if (form == null || !form.validate()) return;
    await widget.onSubmit(
      LoginCredentials(
        username: _usernameController.text,
        password: _passwordController.text,
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Form(
      key: _formKey,
      autovalidateMode: AutovalidateMode.onUserInteraction,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          if (widget.errorText != null) ...[
            _ErrorLine(message: widget.errorText!),
            const SizedBox(height: 16),
          ],
          TextFormField(
            controller: _usernameController,
            enabled: !widget.busy,
            textInputAction: TextInputAction.next,
            autofillHints: const [AutofillHints.username],
            validator: Validators.username,
            decoration: const InputDecoration(
              labelText: Strings.usernameLabel,
              hintText: Strings.username,
              prefixIcon: Icon(Icons.person_outline_rounded),
            ),
          ),
          const SizedBox(height: 14),
          TextFormField(
            controller: _passwordController,
            enabled: !widget.busy,
            obscureText: _obscure,
            textInputAction: TextInputAction.done,
            autofillHints: const [AutofillHints.password],
            onFieldSubmitted: (_) => _submit(),
            validator: Validators.password,
            decoration: InputDecoration(
              labelText: Strings.password,
              prefixIcon: const Icon(Icons.lock_outline_rounded),
              suffixIcon: IconButton(
                tooltip: _obscure ? 'Show password' : 'Hide password',
                onPressed: () => setState(() => _obscure = !_obscure),
                icon: Icon(
                  _obscure
                      ? Icons.visibility_outlined
                      : Icons.visibility_off_outlined,
                ),
              ),
            ),
          ),
          const SizedBox(height: 8),
          Align(
            alignment: Alignment.centerRight,
            child: TextButton(
              onPressed: widget.busy
                  ? null
                  : (widget.onForgotPassword ?? () {}),
              child: const Text(Strings.forgotPassword),
            ),
          ),
          const SizedBox(height: 8),
          AppButton(
            label: widget.busy ? Strings.signingIn : Strings.signIn,
            busy: widget.busy,
            onPressed: widget.busy ? null : _submit,
          ),
          const SizedBox(height: 20),
          Text(
            Strings.loginCredentialHelp,
            textAlign: TextAlign.center,
            style: Theme.of(context).textTheme.bodySmall?.copyWith(
              color: Theme.of(context).colorScheme.onSurfaceVariant,
            ),
          ),
        ],
      ),
    );
  }
}

class _ErrorLine extends StatelessWidget {
  const _ErrorLine({required this.message});

  final String message;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: scheme.errorContainer,
        borderRadius: BorderRadius.circular(12),
      ),
      child: Row(
        children: [
          Icon(
            Icons.error_outline_rounded,
            size: 18,
            color: scheme.onErrorContainer,
          ),
          const SizedBox(width: 8),
          Expanded(
            child: Text(
              message,
              style: TextStyle(fontSize: 13.5, color: scheme.onErrorContainer),
            ),
          ),
        ],
      ),
    );
  }
}
