import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../providers/organizations_provider.dart';

/// Hides a feature when it is disabled for the signed-in user's organization.
/// Errors fail closed; callers can retry loading the organization flags.
class ModuleGate extends ConsumerWidget {
  const ModuleGate({
    super.key,
    required this.moduleKey,
    required this.moduleLabel,
    required this.child,
  });

  final String moduleKey;
  final String moduleLabel;
  final Widget child;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final modules = ref.watch(meModulesProvider);
    return modules.when(
      loading: () =>
          const Scaffold(body: Center(child: CircularProgressIndicator())),
      error: (error, stack) => Scaffold(
        appBar: AppBar(title: Text(moduleLabel)),
        body: Center(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                const Icon(Icons.cloud_off_outlined, size: 40),
                const SizedBox(height: 12),
                const Text('Could not verify feature availability.'),
                const SizedBox(height: 12),
                FilledButton.icon(
                  onPressed: () => ref.invalidate(meModulesProvider),
                  icon: const Icon(Icons.refresh),
                  label: const Text('Retry'),
                ),
              ],
            ),
          ),
        ),
      ),
      data: (enabledModules) => enabledModules.contains(moduleKey)
          ? child
          : Scaffold(
              appBar: AppBar(title: Text(moduleLabel)),
              body: Center(
                child: Padding(
                  padding: const EdgeInsets.all(24),
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      const Icon(Icons.lock_outline, size: 40),
                      const SizedBox(height: 12),
                      Text(
                        '$moduleLabel is not enabled for your organization.',
                      ),
                      const SizedBox(height: 12),
                      FilledButton.tonal(
                        onPressed: () => context.go('/more'),
                        child: const Text('Back to More'),
                      ),
                    ],
                  ),
                ),
              ),
            ),
    );
  }
}
