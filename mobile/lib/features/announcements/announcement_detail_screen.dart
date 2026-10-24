import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/network/api_exception.dart';
import '../../core/strings.dart';
import '../../core/utils/formatters.dart';
import '../../providers/announcements_provider.dart';
import '../../widgets/app_button.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';
import '../../widgets/section_card.dart';

/// Single announcement (FR-EMP-120) with acknowledgement action
/// (`POST /announcements/{id}/acknowledge`).
class AnnouncementDetailScreen extends ConsumerStatefulWidget {
  const AnnouncementDetailScreen({super.key, required this.announcementId});

  final String announcementId;

  @override
  ConsumerState<AnnouncementDetailScreen> createState() =>
      _AnnouncementDetailScreenState();
}

class _AnnouncementDetailScreenState
    extends ConsumerState<AnnouncementDetailScreen> {
  bool _busy = false;
  String? _error;

  Future<void> _acknowledge() async {
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await ref
          .read(announcementActionsProvider)
          .acknowledge(widget.announcementId);
      ref.invalidate(announcementDetailProvider(widget.announcementId));
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text(Strings.announcementAcknowledged)),
      );
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e.message);
    } catch (_) {
      if (mounted) setState(() => _error = Strings.offlineMessage);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final detail = ref.watch(announcementDetailProvider(widget.announcementId));

    return AppScaffold(
      title: Strings.announcements,
      body: RefreshIndicator(
        onRefresh: () async =>
            ref.invalidate(announcementDetailProvider(widget.announcementId)),
        child: AsyncBody(
          value: detail,
          onRetry: () =>
              ref.invalidate(announcementDetailProvider(widget.announcementId)),
          padding: const EdgeInsets.all(16),
          builder: (item) {
            final content = [
              if (item.summary != null && item.summary!.isNotEmpty)
                item.summary!,
              if (item.body != null && item.body!.isNotEmpty) item.body!,
            ].join('\n\n');
            return ListView(
              physics: const AlwaysScrollableScrollPhysics(),
              children: [
                if (item.pinned)
                  Padding(
                    padding: const EdgeInsets.only(bottom: 10),
                    child: Chip(
                      avatar: const Icon(Icons.push_pin_rounded, size: 16),
                      label: const Text(Strings.pinned),
                    ),
                  ),
                Text(
                  item.title,
                  style: Theme.of(context).textTheme.headlineSmall
                      ?.copyWith(fontWeight: FontWeight.w700),
                ),
                const SizedBox(height: 8),
                Wrap(
                  spacing: 8,
                  runSpacing: 6,
                  children: [
                    if (item.category != null)
                      StatusLikeTag(label: item.category!),
                    if (item.audience != null)
                      StatusLikeTag(label: item.audience!),
                    StatusLikeTag(
                      label: AppFormatters.timestamp(item.publishedAt),
                      icon: Icons.schedule_rounded,
                    ),
                    if (item.expiresAt != null)
                      StatusLikeTag(
                        label:
                            '${Strings.expiresOn}: ${AppFormatters.date(item.expiresAt)}',
                        icon: Icons.event_busy_rounded,
                      ),
                  ],
                ),
                const SizedBox(height: 16),
                SectionCard(
                  title: Strings.summary,
                  child: Text(
                    content.isEmpty ? Strings.emptyGeneric : content,
                    style: Theme.of(context).textTheme.bodyMedium
                        ?.copyWith(height: 1.5),
                  ),
                ),
                if (_error != null) ...[
                  const SizedBox(height: 12),
                  Text(
                    _error!,
                    style: TextStyle(
                      color: Theme.of(context).colorScheme.error,
                    ),
                  ),
                ],
                if (item.needsAcknowledgement) ...[
                  const SizedBox(height: 20),
                  AppButton(
                    label: Strings.acknowledge,
                    busy: _busy,
                    icon: Icons.how_to_reg_rounded,
                    onPressed: _busy ? null : _acknowledge,
                  ),
                ] else if (item.acknowledgedAt != null) ...[
                  const SizedBox(height: 16),
                  Row(
                    children: [
                      Icon(
                        Icons.check_circle_rounded,
                        size: 18,
                        color: Theme.of(context).colorScheme.primary,
                      ),
                      const SizedBox(width: 8),
                      const Text(Strings.acknowledged),
                    ],
                  ),
                ],
                const SizedBox(height: 32),
              ],
            );
          },
        ),
      ),
    );
  }
}

/// Compact metadata tag used on announcement detail.
class StatusLikeTag extends StatelessWidget {
  const StatusLikeTag({super.key, required this.label, this.icon});

  final String label;
  final IconData? icon;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 9, vertical: 5),
      decoration: BoxDecoration(
        color: scheme.surfaceContainerHighest,
        borderRadius: BorderRadius.circular(8),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          if (icon != null) ...[
            Icon(icon, size: 13, color: scheme.onSurfaceVariant),
            const SizedBox(width: 5),
          ],
          Text(
            label,
            style: Theme.of(context).textTheme.labelSmall
                ?.copyWith(color: scheme.onSurfaceVariant),
          ),
        ],
      ),
    );
  }
}
