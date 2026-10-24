import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/network/api_exception.dart';
import '../../core/strings.dart';
import '../../providers/auth_provider.dart';
import '../../widgets/app_button.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/form_error.dart';

/// MFA verification (FR-EMP-003): `POST /auth/mfa/verify` with the code from
/// the authenticator app. Entered automatically at 6 digits.
class MfaScreen extends ConsumerStatefulWidget {
  const MfaScreen({super.key});

  @override
  ConsumerState<MfaScreen> createState() => _MfaScreenState();
}

class _MfaScreenState extends ConsumerState<MfaScreen> {
  final _codeController = TextEditingController();
  final _focus = FocusNode();
  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    _codeController.dispose();
    _focus.dispose();
    super.dispose();
  }

  Future<void> _verify([String? code]) async {
    final value = (code ?? _codeController.text).trim();
    if (value.length < 6) {
      setState(() => _error = 'Enter the 6-digit code');
      return;
    }
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await ref.read(authControllerProvider.notifier).verifyMfa(value);
    } on ApiException catch (e) {
      setState(
        () => _error = e.statusCode == 401 ? Strings.mfaInvalidCode : e.message,
      );
    } catch (_) {
      setState(() => _error = Strings.offlineMessage);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return AppScaffold(
      title: Strings.mfaTitle,
      showBell: false,
      leading: IconButton(
        tooltip: Strings.back,
        icon: const Icon(Icons.arrow_back_rounded),
        onPressed: () => ref.read(authControllerProvider.notifier).cancelMfa(),
      ),
      body: SafeArea(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const SizedBox(height: 16),
              Icon(
                Icons.security_rounded,
                size: 56,
                color: Theme.of(context).colorScheme.primary,
              ),
              const SizedBox(height: 16),
              Text(
                Strings.mfaTitle,
                style: Theme.of(context).textTheme.titleLarge
                    ?.copyWith(fontWeight: FontWeight.w700),
                textAlign: TextAlign.center,
              ),
              const SizedBox(height: 8),
              Text(
                Strings.mfaSubtitle,
                style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                  color: Theme.of(context).colorScheme.onSurfaceVariant,
                ),
                textAlign: TextAlign.center,
              ),
              const SizedBox(height: 28),
              if (_error != null) ...[FormErrorBanner(message: _error!)],
              TextField(
                controller: _codeController,
                focusNode: _focus,
                enabled: !_busy,
                autofocus: true,
                keyboardType: TextInputType.number,
                maxLength: 6,
                textAlign: TextAlign.center,
                style: const TextStyle(
                  fontSize: 28,
                  letterSpacing: 12,
                  fontWeight: FontWeight.w700,
                ),
                decoration: InputDecoration(
                  counterText: '',
                  labelText: Strings.mfaCodeLabel,
                  errorText: _error,
                ),
                onChanged: (value) {
                  if (value.length == 6) _verify(value);
                },
                onSubmitted: (_) => _verify(),
              ),
              const SizedBox(height: 24),
              AppButton(
                label: Strings.mfaVerify,
                busy: _busy,
                onPressed: _busy ? null : () => _verify(),
              ),
              const SizedBox(height: 12),
              TextButton(
                onPressed: _busy
                    ? null
                    : () =>
                          ref.read(authControllerProvider.notifier).cancelMfa(),
                child: const Text('Use a different account'),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
