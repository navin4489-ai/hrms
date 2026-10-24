import 'package:flutter/material.dart';

import '../../core/strings.dart';
import '../../theme/app_theme.dart';
import '../../widgets/wintech_logo.dart';

/// First route while the stored session is being restored (FR-EMP-005).
/// The router redirect moves to /login or /home once the status flips.
class SplashScreen extends StatelessWidget {
  const SplashScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    return Scaffold(
      body: Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const WintechLogo(size: 88),
            const SizedBox(height: 20),
            Text(
              Strings.appName,
              style: theme.textTheme.headlineSmall?.copyWith(
                fontWeight: FontWeight.w700,
              ),
            ),
            const SizedBox(height: 4),
            Text(
              'People & work, in sync',
              style: theme.textTheme.bodyMedium?.copyWith(
                color: AppColors.brand,
                fontWeight: FontWeight.w600,
              ),
            ),
            const SizedBox(height: 32),
            const CircularProgressIndicator(),
            const SizedBox(height: 16),
            Text(
              'Restoring your session…',
              style: theme.textTheme.bodySmall?.copyWith(
                color: scheme.onSurfaceVariant,
              ),
            ),
          ],
        ),
      ),
    );
  }
}
