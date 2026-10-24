import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/strings.dart';
import '../../providers/auth_provider.dart';
import '../../widgets/app_button.dart';
import '../../widgets/app_scaffold.dart';

/// Blocking screen for a suspended/terminated account (FR-EMP-006):
/// credentials were correct but the account is inactive (403).
class AccountBlockedScreen extends ConsumerWidget {
  const AccountBlockedScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final auth = ref.watch(authControllerProvider);
    final reason = (auth.blockedReason?.trim().isNotEmpty ?? false)
        ? auth.blockedReason!
        : Strings.accountNotActiveDetail;

    return AppScaffold(
      title: Strings.accountNotActiveTitle,
      showBell: false,
      body: SafeArea(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const Spacer(),
              Container(
                padding: const EdgeInsets.all(20),
                decoration: BoxDecoration(
                  color: scheme.errorContainer,
                  shape: BoxShape.circle,
                ),
                child: Icon(
                  Icons.block_rounded,
                  size: 44,
                  color: scheme.onErrorContainer,
                ),
              ),
              const SizedBox(height: 20),
              Text(
                Strings.accountNotActiveTitle,
                style: theme.textTheme.titleLarge?.copyWith(
                  fontWeight: FontWeight.w700,
                ),
                textAlign: TextAlign.center,
              ),
              const SizedBox(height: 10),
              Text(
                reason,
                style: theme.textTheme.bodyMedium?.copyWith(
                  color: scheme.onSurfaceVariant,
                ),
                textAlign: TextAlign.center,
              ),
              const SizedBox(height: 8),
              Text(
                'Contact HR if you believe this is a mistake.',
                style: theme.textTheme.bodySmall?.copyWith(
                  color: scheme.onSurfaceVariant,
                ),
                textAlign: TextAlign.center,
              ),
              const Spacer(),
              AppButton(
                label: Strings.signOut,
                icon: Icons.logout_rounded,
                onPressed: () async {
                  await ref.read(authControllerProvider.notifier).logout();
                  if (context.mounted) context.go('/login');
                },
              ),
            ],
          ),
        ),
      ),
    );
  }
}
