import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:firebase_messaging/firebase_messaging.dart';

import 'app.dart';
import 'core/network/push_notifications.dart';
import 'providers/auth_provider.dart';

/// Bootstrap: init bindings, run app, then restore the stored session
/// (FR-EMP-005 secure token storage; silent refresh handles expired access
/// tokens afterwards).
Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();

  // Surface uncaught errors in debug for easier diagnosis.
  FlutterError.onError = (details) {
    FlutterError.presentError(details);
    if (kDebugMode) {
      debugPrint('Unhandled Flutter error: ${details.exceptionAsString()}');
    }
  };

  // Initialize push notifications (safe no-op when Firebase isn't configured).
  try {
    FirebaseMessaging.onBackgroundMessage(firebaseBackgroundHandler);
  } catch (_) {}
  await PushNotifications.instance.init();

  final container = ProviderContainer();
  runApp(
    UncontrolledProviderScope(
      container: container,
      child: const _SessionRestoringApp(),
    ),
  );
}

/// Kicks off session restore exactly once, then shows the app.
class _SessionRestoringApp extends ConsumerStatefulWidget {
  const _SessionRestoringApp();

  @override
  ConsumerState<_SessionRestoringApp> createState() =>
      _SessionRestoringAppState();
}

class _SessionRestoringAppState extends ConsumerState<_SessionRestoringApp> {
  @override
  void initState() {
    super.initState();
    // Fire-and-forget: the splash route renders while status == restoring.
    Future.microtask(
      () => ref.read(authControllerProvider.notifier).restoreSession(),
    );
  }

  @override
  Widget build(BuildContext context) => const WintechApp();
}
