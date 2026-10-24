import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/network/api_exception.dart';
import '../../core/strings.dart';
import '../../providers/auth_provider.dart';
import '../../theme/app_theme.dart';
import '../../widgets/wintech_logo.dart';
import 'login_form.dart';

/// Sign-in screen (FR-EMP-001/006): username + password, credential guidance,
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
    final scheme = theme.colorScheme;
    final auth = ref.watch(authControllerProvider);
    final sessionExpired =
        auth.sessionExpired && auth.status == AuthStatus.unauthenticated;

    return Scaffold(
      backgroundColor: scheme.surface,
      body: SafeArea(
        bottom: false,
        child: SingleChildScrollView(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Container(
                padding: const EdgeInsets.fromLTRB(24, 24, 24, 34),
                decoration: const BoxDecoration(
                  gradient: LinearGradient(
                    begin: Alignment.topLeft,
                    end: Alignment.bottomRight,
                    colors: [AppColors.brandDark, AppColors.brand],
                  ),
                  borderRadius: BorderRadius.vertical(
                    bottom: Radius.circular(32),
                  ),
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    const WintechLogo(
                      size: 56,
                      showWordmark: true,
                      wordmarkColor: Colors.white,
                    ),
                    const SizedBox(height: 30),
                    Text(
                      'People operations,\nmade simple.',
                      style: theme.textTheme.headlineMedium?.copyWith(
                        color: Colors.white,
                        fontWeight: FontWeight.w800,
                        height: 1.08,
                        letterSpacing: -0.5,
                      ),
                    ),
                    const SizedBox(height: 10),
                    Text(
                      'A better workday starts here.',
                      style: theme.textTheme.bodyLarge?.copyWith(
                        color: Colors.white.withValues(alpha: 0.78),
                      ),
                    ),
                  ],
                ),
              ),
              Padding(
                padding: const EdgeInsets.fromLTRB(20, 24, 20, 36),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Text(
                      'Welcome back',
                      style: theme.textTheme.headlineSmall?.copyWith(
                        fontWeight: FontWeight.w800,
                        letterSpacing: -0.3,
                      ),
                    ),
                    const SizedBox(height: 6),
                    Text(
                      'Sign in to access your HR self-service.',
                      style: theme.textTheme.bodyMedium?.copyWith(
                        color: scheme.onSurfaceVariant,
                      ),
                    ),
                    const SizedBox(height: 20),
                    if (sessionExpired) ...[
                      _SessionExpiredBanner(
                        onDismiss: () => ref
                            .read(authControllerProvider.notifier)
                            .acknowledgeSessionExpired(),
                      ),
                      const SizedBox(height: 14),
                    ],
                    Card(
                      child: Padding(
                        padding: const EdgeInsets.all(18),
                        child: LoginForm(
                          busy: _busy,
                          errorText: _errorText,
                          onSubmit: _submit,
                          onForgotPassword: () =>
                              context.push('/forgot-password'),
                        ),
                      ),
                    ),
                    const SizedBox(height: 20),
                    Row(
                      mainAxisAlignment: MainAxisAlignment.center,
                      children: [
                        Icon(
                          Icons.shield_outlined,
                          size: 16,
                          color: scheme.primary,
                        ),
                        const SizedBox(width: 7),
                        Text(
                          'Your workplace, securely connected',
                          style: theme.textTheme.bodySmall?.copyWith(
                            color: scheme.onSurfaceVariant,
                            fontWeight: FontWeight.w600,
                          ),
                        ),
                      ],
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
