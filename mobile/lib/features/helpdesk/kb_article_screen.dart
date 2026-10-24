import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/strings.dart';
import '../../core/utils/formatters.dart';
import '../../providers/cases_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';
import '../../widgets/section_card.dart';

/// A single FAQ / knowledge-base article (FR-EMP-115).
class KbArticleScreen extends ConsumerWidget {
  const KbArticleScreen({super.key, required this.articleId});

  final String articleId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final article = ref.watch(kbArticleProvider(articleId));

    return AppScaffold(
      title: Strings.faq,
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(kbArticleProvider(articleId)),
        child: AsyncBody(
          value: article,
          onRetry: () => ref.invalidate(kbArticleProvider(articleId)),
          padding: const EdgeInsets.all(16),
          builder: (item) => ListView(
            physics: const AlwaysScrollableScrollPhysics(),
            children: [
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
                  if (item.category != null) _Tag(label: item.category!),
                  if (item.version != null) _Tag(label: 'v${item.version!}'),
                  if (item.expiresAt != null)
                    _Tag(
                      label:
                          '${Strings.expiresOn}: ${AppFormatters.date(item.expiresAt)}',
                      icon: Icons.event_busy_rounded,
                    ),
                ],
              ),
              const SizedBox(height: 16),
              SectionCard(
                title: Strings.faq,
                child: Text(
                  (item.body == null || item.body!.trim().isEmpty)
                      ? Strings.emptyGeneric
                      : item.body!,
                  style: Theme.of(context).textTheme.bodyMedium
                      ?.copyWith(height: 1.55),
                ),
              ),
              const SizedBox(height: 32),
            ],
          ),
        ),
      ),
    );
  }
}

class _Tag extends StatelessWidget {
  const _Tag({required this.label, this.icon});

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
