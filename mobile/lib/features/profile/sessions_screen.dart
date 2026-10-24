import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/network/api_exception.dart';
import '../../core/strings.dart';
import '../../core/utils/formatters.dart';
import '../../providers/profile_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';
import '../../widgets/confirm_dialog.dart';

/// Active login sessions with remote revocation (FR-EMP-004).
class SessionsScreen extends ConsumerStatefulWidget {
  const SessionsScreen({super.key});

  @override
  ConsumerState<SessionsScreen> createState() => _SessionsScreenState();
}

class _SessionsScreenState extends ConsumerState<SessionsScreen> {
  String? _revokingId;

  Future<void> _revoke(String id) async {
    final confirmed = await showConfirmDialog(
      context,
      title: Strings.revokeSession,
      message: Strings.revokeSessionConfirm,
      confirmLabel: Strings.revokeSession,
      destructive: true,
    );
    if (!confirmed || !mounted) return;

    setState(() => _revokingId = id);
    try {
      await ref.read(profileActionsProvider).revokeSession(id);
      ref.invalidate(sessionsProvider);
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(const SnackBar(content: Text('Session revoked')));
      }
    } on ApiException catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text(e.detail ?? e.message)));
      }
    } catch (_) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(const SnackBar(content: Text(Strings.offlineMessage)));
      }
    } finally {
      if (mounted) setState(() => _revokingId = null);
    }
  }

  @override
  Widget build(BuildContext context) {
    final sessions = ref.watch(sessionsProvider);

    return AppScaffold(
      title: Strings.sessions,
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(sessionsProvider),
        child: AsyncBody(
          value: sessions,
          onRetry: () => ref.invalidate(sessionsProvider),
          isEmpty: (items) => items.isEmpty,
          emptyTitle: 'No sessions',
          emptyMessage: 'Signed-in devices will appear here.',
          emptyIcon: Icons.devices_outlined,
          builder: (items) => ListView.separated(
            physics: const AlwaysScrollableScrollPhysics(),
            padding: const EdgeInsets.all(16),
            itemCount: items.length,
            separatorBuilder: (_, __) => const SizedBox(height: 10),
            itemBuilder: (context, index) {
              final session = items[index];
              return Card(
                clipBehavior: Clip.antiAlias,
                child: ListTile(
                  leading: Icon(
                    session.current
                        ? Icons.smartphone_rounded
                        : Icons.devices_other_rounded,
                  ),
                  title: Row(
                    children: [
                      Flexible(
                        child: Text(
                          session.deviceName,
                          overflow: TextOverflow.ellipsis,
                        ),
                      ),
                      if (session.current) ...[
                        const SizedBox(width: 8),
                        Chip(
                          label: const Text(Strings.thisDevice),
                          visualDensity: VisualDensity.compact,
                          materialTapTargetSize:
                              MaterialTapTargetSize.shrinkWrap,
                        ),
                      ],
                    ],
                  ),
                  subtitle: Text(
                    [
                      if (session.ipAddress != null) session.ipAddress!,
                      'Last used ${AppFormatters.relativeTime(session.lastUsedAt)}',
                      if (session.createdAt != null)
                        'Signed in ${AppFormatters.timestamp(session.createdAt)}',
                    ].join(' · '),
                    style: Theme.of(context).textTheme.bodySmall,
                  ),
                  trailing: session.current
                      ? null
                      : TextButton(
                          onPressed: _revokingId != null
                              ? null
                              : () => _revoke(session.id),
                          child: _revokingId == session.id
                              ? const SizedBox(
                                  width: 16,
                                  height: 16,
                                  child: CircularProgressIndicator(
                                    strokeWidth: 2,
                                  ),
                                )
                              : const Text(Strings.revokeSession),
                        ),
                ),
              );
            },
          ),
        ),
      ),
    );
  }
}
