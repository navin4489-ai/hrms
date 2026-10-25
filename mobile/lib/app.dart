import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../core/strings.dart';
import '../features/chat/call_overlay.dart';
import '../router/app_router.dart';
import '../theme/app_theme.dart';

/// Root application widget: MaterialApp.router + theme + global call overlay.
class WintechApp extends ConsumerWidget {
  const WintechApp({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final router = ref.watch(routerProvider);
    return MaterialApp.router(
      title: Strings.appName,
      debugShowCheckedModeBanner: false,
      theme: AppTheme.light(),
      darkTheme: AppTheme.dark(),
      themeMode: ThemeMode.system,
      routerConfig: router,
      builder: (context, child) => Stack(
        children: [
          if (child != null) child,
          // The call UI (full-screen or floating mini) renders above all routes.
          const CallOverlay(),
        ],
      ),
    );
  }
}
