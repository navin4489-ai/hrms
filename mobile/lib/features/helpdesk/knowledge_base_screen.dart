import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/strings.dart';
import '../../core/utils/formatters.dart';
import '../../providers/cases_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';

/// Searchable FAQ / knowledge base (FR-EMP-115).
class KnowledgeBaseScreen extends ConsumerStatefulWidget {
  const KnowledgeBaseScreen({super.key});

  @override
  ConsumerState<KnowledgeBaseScreen> createState() =>
      _KnowledgeBaseScreenState();
}

class _KnowledgeBaseScreenState extends ConsumerState<KnowledgeBaseScreen> {
  final _controller = TextEditingController();
  Timer? _debounce;
  String _query = '';

  @override
  void dispose() {
    _debounce?.cancel();
    _controller.dispose();
    super.dispose();
  }

  void _onChanged(String value) {
    // Keep the clear-button visibility in sync while typing.
    setState(() {});
    _debounce?.cancel();
    _debounce = Timer(const Duration(milliseconds: 350), () {
      if (!mounted || _query == value.trim()) return;
      setState(() => _query = value.trim());
    });
  }

  @override
  Widget build(BuildContext context) {
    final articles = ref.watch(knowledgeBaseProvider(_query));

    return AppScaffold(
      title: Strings.faq,
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(knowledgeBaseProvider(_query)),
        child: Column(
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 12, 16, 4),
              child: TextField(
                controller: _controller,
                textInputAction: TextInputAction.search,
                decoration: InputDecoration(
                  hintText: Strings.search,
                  prefixIcon: const Icon(Icons.search_rounded),
                  suffixIcon: _controller.text.isEmpty
                      ? null
                      : IconButton(
                          tooltip: Strings.cancel,
                          icon: const Icon(Icons.clear_rounded),
                          onPressed: () {
                            _controller.clear();
                            setState(() => _query = '');
                          },
                        ),
                ),
                onChanged: _onChanged,
              ),
            ),
            Expanded(
              child: AsyncBody(
                value: articles,
                onRetry: () => ref.invalidate(knowledgeBaseProvider(_query)),
                isEmpty: (items) => items.isEmpty,
                emptyTitle: Strings.noFaq,
                emptyMessage: _query.isEmpty
                    ? Strings.emptyGeneric
                    : Strings.noResultsHint,
                emptyIcon: Icons.menu_book_outlined,
                builder: (items) => ListView.separated(
                  physics: const AlwaysScrollableScrollPhysics(),
                  padding: const EdgeInsets.fromLTRB(16, 8, 16, 16),
                  itemCount: items.length,
                  separatorBuilder: (_, __) => const SizedBox(height: 10),
                  itemBuilder: (context, index) {
                    final article = items[index];
                    return Card(
                      clipBehavior: Clip.antiAlias,
                      child: ListTile(
                        leading: const Icon(Icons.article_outlined),
                        title: Text(
                          article.title,
                          maxLines: 2,
                          overflow: TextOverflow.ellipsis,
                        ),
                        subtitle: Text(
                          [
                            if (article.category != null) article.category!,
                            if (article.helpfulCount > 0)
                              '${article.helpfulCount} helpful',
                            AppFormatters.relativeTime(article.updatedAt),
                          ].join(' · '),
                        ),
                        onTap: () =>
                            context.go('/more/cases/faq/${article.id}'),
                      ),
                    );
                  },
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
