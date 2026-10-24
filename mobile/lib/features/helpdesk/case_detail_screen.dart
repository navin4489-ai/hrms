import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/network/api_exception.dart';
import '../../core/strings.dart';
import '../../core/utils/formatters.dart';
import '../../models/case_models.dart';
import '../../providers/cases_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';
import '../../widgets/confirm_dialog.dart';
import '../../widgets/section_card.dart';
import '../../widgets/status_chip.dart';

/// Case detail: message thread, employee replies (FR-EMP-113), reopen and
/// resolution rating.
class CaseDetailScreen extends ConsumerStatefulWidget {
  const CaseDetailScreen({super.key, required this.caseId});

  final String caseId;

  @override
  ConsumerState<CaseDetailScreen> createState() => _CaseDetailScreenState();
}

class _CaseDetailScreenState extends ConsumerState<CaseDetailScreen> {
  final _replyController = TextEditingController();
  bool _sending = false;
  bool _acting = false;

  @override
  void dispose() {
    _replyController.dispose();
    super.dispose();
  }

  void _snack(String message) {
    if (mounted) {
      ScaffoldMessenger.of(context)
          .showSnackBar(SnackBar(content: Text(message)));
    }
  }

  Future<void> _sendReply() async {
    final body = _replyController.text.trim();
    if (body.isEmpty) return;
    FocusScope.of(context).unfocus();
    setState(() => _sending = true);
    try {
      await ref
          .read(caseActionsProvider)
          .reply(caseId: widget.caseId, body: body);
      _replyController.clear();
      ref.invalidate(caseDetailProvider(widget.caseId));
      _snack(Strings.replySent);
    } on ApiException catch (e) {
      _snack(e.detail ?? e.message);
    } catch (_) {
      _snack(Strings.offlineMessage);
    } finally {
      if (mounted) setState(() => _sending = false);
    }
  }

  Future<void> _reopen() async {
    final confirmed = await showConfirmDialog(
      context,
      title: Strings.reopen,
      message: 'Reopen this case so the helpdesk can act on it again?',
      confirmLabel: Strings.reopen,
    );
    if (!confirmed || !mounted) return;
    setState(() => _acting = true);
    try {
      await ref
          .read(caseActionsProvider)
          .transition(caseId: widget.caseId, action: 'Reopen');
      ref.invalidate(caseDetailProvider(widget.caseId));
      ref.invalidate(casesProvider);
      _snack('Case reopened');
    } on ApiException catch (e) {
      _snack(e.detail ?? e.message);
    } catch (_) {
      _snack(Strings.offlineMessage);
    } finally {
      if (mounted) setState(() => _acting = false);
    }
  }

  Future<void> _rate(CaseDetail detail) async {
    var rating = 5;
    var comment = '';
    await showDialog<void>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) => AlertDialog(
          title: const Text(Strings.rateCase),
          content: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Text(
                detail.subject,
                style: Theme.of(dialogContext).textTheme.bodyMedium,
              ),
              const SizedBox(height: 12),
              Row(
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  for (var star = 1; star <= 5; star++)
                    IconButton(
                      tooltip: '$star star${star == 1 ? '' : 's'}',
                      onPressed: () => setDialogState(() => rating = star),
                      icon: Icon(
                        star <= rating
                            ? Icons.star_rounded
                            : Icons.star_outline_rounded,
                        color: star <= rating
                            ? Colors.amber
                            : Theme.of(dialogContext)
                                  .colorScheme
                                  .onSurfaceVariant,
                      ),
                    ),
                ],
              ),
              TextField(
                maxLines: 2,
                decoration: const InputDecoration(
                  labelText: Strings.ratingComment,
                ),
                onChanged: (value) => comment = value,
              ),
            ],
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.of(dialogContext).pop(),
              child: const Text(Strings.cancel),
            ),
            FilledButton(
              onPressed: () => Navigator.of(dialogContext).pop(),
              child: const Text(Strings.send),
            ),
          ],
        ),
      ),
    );
    if (!mounted) return;
    setState(() => _acting = true);
    try {
      await ref
          .read(caseActionsProvider)
          .rate(
            caseId: widget.caseId,
            rating: rating,
            comment: comment.trim().isEmpty ? null : comment.trim(),
          );
      ref.invalidate(caseDetailProvider(widget.caseId));
      _snack('Thanks for rating this case');
    } on ApiException catch (e) {
      _snack(e.detail ?? e.message);
    } catch (_) {
      _snack(Strings.offlineMessage);
    } finally {
      if (mounted) setState(() => _acting = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final detail = ref.watch(caseDetailProvider(widget.caseId));
    final theme = Theme.of(context);

    return AppScaffold(
      title: Strings.caseDetail,
      body: RefreshIndicator(
        onRefresh: () async =>
            ref.invalidate(caseDetailProvider(widget.caseId)),
        child: AsyncBody(
          value: detail,
          onRetry: () => ref.invalidate(caseDetailProvider(widget.caseId)),
          builder: (data) => SingleChildScrollView(
            physics: const AlwaysScrollableScrollPhysics(),
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Expanded(
                      child: Text(
                        data.subject,
                        style: theme.textTheme.titleLarge?.copyWith(
                          fontWeight: FontWeight.w700,
                        ),
                      ),
                    ),
                    StatusChip(status: data.status, dense: true),
                  ],
                ),
                const SizedBox(height: 4),
                Text(
                  '${Strings.ticketNumber} ${data.ticketNumber}',
                  style: theme.textTheme.bodySmall?.copyWith(
                    color: theme.colorScheme.onSurfaceVariant,
                  ),
                ),
                const SizedBox(height: 16),
                SectionCard(
                  title: Strings.summary,
                  child: Column(
                    children: [
                      if (data.category != null)
                        InfoRow(label: Strings.category, value: data.category!),
                      if (data.subcategory != null)
                        InfoRow(
                          label: Strings.subcategory,
                          value: data.subcategory!,
                        ),
                      InfoRow(
                        label: Strings.priority,
                        value: AppFormatters.enumLabel(data.priority),
                      ),
                      InfoRow(
                        label: Strings.urgency,
                        value: AppFormatters.enumLabel(data.priority),
                      ),
                      if (data.assigneeName != null)
                        InfoRow(
                          label: 'Assigned to',
                          value: data.assigneeName!,
                        ),
                      InfoRow(
                        label: 'Created',
                        value: AppFormatters.timestamp(data.createdAt),
                      ),
                      if (data.slaDueAt != null)
                        InfoRow(
                          label: 'First response due',
                          value: AppFormatters.timestamp(data.slaDueAt),
                        ),
                      if (data.resolvedAt != null)
                        InfoRow(
                          label: 'Resolved',
                          value: AppFormatters.timestamp(data.resolvedAt),
                        ),
                      if (data.description != null &&
                          data.description!.isNotEmpty)
                        InfoRow(
                          label: Strings.description,
                          value: data.description!,
                        ),
                    ],
                  ),
                ),
                if (data.messages.isNotEmpty) ...[
                  const SizedBox(height: 12),
                  SectionCard(
                    title: Strings.replies,
                    child: Column(
                      children: [
                        for (final message in data.messages)
                          _MessageTile(message: message),
                      ],
                    ),
                  ),
                ],
                const SizedBox(height: 12),
                if (data.canReply) ...[
                  TextField(
                    controller: _replyController,
                    enabled: !_sending,
                    maxLines: 3,
                    maxLength: 2000,
                    decoration: const InputDecoration(
                      labelText: Strings.replyPlaceholder,
                      alignLabelWithHint: true,
                      hintText: Strings.replyPlaceholder,
                    ),
                  ),
                  const SizedBox(height: 12),
                  SizedBox(
                    width: double.infinity,
                    child: FilledButton.icon(
                      onPressed: _sending ? null : _sendReply,
                      icon: _sending
                          ? const SizedBox(
                              width: 18,
                              height: 18,
                              child: CircularProgressIndicator(
                                strokeWidth: 2.4,
                              ),
                            )
                          : const Icon(Icons.send_rounded, size: 18),
                      label: const Text(Strings.send),
                    ),
                  ),
                ],
                if (data.canReopen || data.canRate) ...[
                  const SizedBox(height: 12),
                  Row(
                    children: [
                      if (data.canReopen)
                        Expanded(
                          child: OutlinedButton.icon(
                            onPressed: _acting ? null : _reopen,
                            icon: const Icon(Icons.undo_rounded, size: 18),
                            label: const Text(Strings.reopen),
                          ),
                        ),
                      if (data.canReopen && data.canRate)
                        const SizedBox(width: 12),
                      if (data.canRate)
                        Expanded(
                          child: FilledButton.tonalIcon(
                            onPressed: _acting ? null : () => _rate(data),
                            icon: const Icon(
                              Icons.star_outline_rounded,
                              size: 18,
                            ),
                            label: const Text(Strings.rateCase),
                          ),
                        ),
                    ],
                  ),
                ],
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _MessageTile extends StatelessWidget {
  const _MessageTile({required this.message});

  final CaseMessage message;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final isEmployee =
        message.senderRole == null || message.senderRole == 'Employee';
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 8),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              CircleAvatar(
                radius: 14,
                backgroundColor: isEmployee
                    ? scheme.primaryContainer
                    : scheme.tertiaryContainer,
                child: Text(
                  message.senderName.isEmpty
                      ? '?'
                      : message.senderName
                            .trim()
                            .split(RegExp(r'\s+'))
                            .map((part) => part.isEmpty ? '' : part[0])
                            .take(2)
                            .join(),
                  style: TextStyle(
                    fontSize: 11,
                    fontWeight: FontWeight.w700,
                    color: isEmployee
                        ? scheme.onPrimaryContainer
                        : scheme.onTertiaryContainer,
                  ),
                ),
              ),
              const SizedBox(width: 8),
              Expanded(
                child: Text(
                  message.senderName,
                  style: theme.textTheme.bodySmall?.copyWith(
                    fontWeight: FontWeight.w700,
                  ),
                ),
              ),
              Text(
                AppFormatters.relativeTime(message.createdAt),
                style: theme.textTheme.bodySmall?.copyWith(
                  color: scheme.onSurfaceVariant,
                ),
              ),
            ],
          ),
          const SizedBox(height: 6),
          Container(
            width: double.infinity,
            padding: const EdgeInsets.all(12),
            decoration: BoxDecoration(
              color: scheme.surfaceContainerHighest.withValues(alpha: 0.55),
              borderRadius: BorderRadius.circular(12),
            ),
            child: Text(message.body),
          ),
        ],
      ),
    );
  }
}
