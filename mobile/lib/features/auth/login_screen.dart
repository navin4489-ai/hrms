import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/network/api_exception.dart';
import '../../core/strings.dart';
import '../../providers/auth_provider.dart';
import '../../widgets/app_scaffold.dart';
import 'login_form.dart';

/// Sign-in screen (FR-EMP-001/006): username + password, demo hint,
/// session-expired banner, generic 401 message, MFA hand-off.
class LoginScreen extends ConsumerStatefulWidget {
  const LoginScreen({super.key});

  @override
  ConsumerState<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends ConsumerState<LoginScreen> {
  bool _busy = false;
  String? _errorText;

  Future<void> _submit(LoginCredentials credentials) async {
    setState(() {
      _busy = true;
      _errorText = null;
    });
    final controller = ref.read(authControllerProvider.notifier);
    controller.acknowledgeSessionExpired();
    try {
      await controller.login(
        username: credentials.username,
        password: credentials.password,
      );
      // On success / MFA the router redirect moves us (state-driven).
    } on ApiException catch (e) {
      setState(() => _errorText = _friendly(e));
    } catch (_) {
      setState(() => _errorText = Strings.offlineMessage);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  String _friendly(ApiException e) {
    if (e.statusCode == 401) return Strings.invalidCredentials;
    if (e.isTooManyRequests) {
      final seconds = e.retryAfterSeconds;
      return seconds == null
          ? 'Too many attempts. Please wait a moment and try again.'
          : 'Too many attempts. Try again in $seconds seconds.';
    }
    if (e.isAccountInactive) return e.detail ?? Strings.accountNotActiveDetail;
    return e.message;
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final auth = ref.watch(authControllerProvider);
    final sessionExpired =
        auth.sessionExpired && auth.status == AuthStatus.unauthenticated;

    return AppScaffold(
      title: Strings.appName,
      showBell: false,
      body: SafeArea(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(24),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const SizedBox(height: 24),
              _LogoMark(color: theme.colorScheme.primary),
              const SizedBox(height: 20),
              Text(
                'Welcome back',
                style: theme.textTheme.headlineSmall?.copyWith(
                  fontWeight: FontWeight.w700,
                ),
                textAlign: TextAlign.center,
              ),
              const SizedBox(height: 6),
              Text(
                'Sign in to access your HR self-service',
                style: theme.textTheme.bodyMedium?.copyWith(
                  color: theme.colorScheme.onSurfaceVariant,
                ),
                textAlign: TextAlign.center,
              ),
              const SizedBox(height: 28),
              if (sessionExpired) ...[
                _SessionExpiredBanner(
                  onDismiss: () => ref
                      .read(authControllerProvider.notifier)
                      .acknowledgeSessionExpired(),
                ),
                const SizedBox(height: 16),
              ],
              LoginForm(
                busy: _busy,
                errorText: _errorText,
                onSubmit: _submit,
                onForgotPassword: () => context.push('/forgot-password'),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _LogoMark extends StatelessWidget {
  const _LogoMark({required this.color});

  final Color color;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Container(
        width: 84,
        height: 84,
        decoration: BoxDecoration(
          color: color,
          borderRadius: BorderRadius.circular(24),
        ),
        child: const Center(
          child: Text(
            'W',
            style: TextStyle(
              color: Colors.white,
              fontSize: 44,
              fontWeight: FontWeight.w800,
            ),
          ),
        ),
      ),
    );
  }
}

class _SessionExpiredBanner extends StatelessWidget {
  const _SessionExpiredBanner({required this.onDismiss});

  final VoidCallback onDismiss;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: scheme.tertiaryContainer,
        borderRadius: BorderRadius.circular(12),
      ),
      child: Row(
        children: [
          Icon(
            Icons.schedule_rounded,
            size: 18,
            color: scheme.onTertiaryContainer,
          ),
          const SizedBox(width: 8),
          Expanded(
            child: Text(
              Strings.sessionExpired,
              style: TextStyle(
                fontSize: 13.5,
                color: scheme.onTertiaryContainer,
              ),
            ),
          ),
          IconButton(
            tooltip: Strings.close,
            onPressed: onDismiss,
            icon: Icon(
              Icons.close_rounded,
              size: 18,
              color: scheme.onTertiaryContainer,
            ),
          ),
        ],
      ),
    );
  }
}
