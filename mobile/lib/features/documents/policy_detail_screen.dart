import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/network/api_exception.dart';
import '../../core/strings.dart';
import '../../core/utils/file_saver.dart';
import '../../core/utils/formatters.dart';
import '../../providers/documents_provider.dart';
import '../../widgets/app_button.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';
import '../../widgets/confirm_dialog.dart';
import '../../widgets/section_card.dart';
import '../../widgets/status_chip.dart';

/// Policy detail with versioned acknowledgement (FR-EMP-074).
class PolicyDetailScreen extends ConsumerStatefulWidget {
  const PolicyDetailScreen({super.key, required this.policyId});

  final String policyId;

  @override
  ConsumerState<PolicyDetailScreen> createState() => _PolicyDetailScreenState();
}

class _PolicyDetailScreenState extends ConsumerState<PolicyDetailScreen> {
  bool _busy = false;
  bool _downloading = false;

  Future<void> _acknowledge() async {
    final confirmed = await showConfirmDialog(
      context,
      title: Strings.acknowledge,
      message: Strings.acknowledgeConfirm,
      confirmLabel: Strings.acknowledge,
    );
    if (!confirmed || !mounted) return;

    setState(() => _busy = true);
    try {
      await ref
          .read(documentActionsProvider)
          .acknowledgePolicy(id: widget.policyId);
      ref.invalidate(policyDetailProvider(widget.policyId));
      ref.invalidate(policiesProvider);
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text(Strings.policyAcknowledged)),
        );
      }
    } on ApiException catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(
              e.isConflict
                  ? Strings.policyAlreadyAcknowledged
                  : (e.detail ?? e.message),
            ),
          ),
        );
      }
    } catch (_) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(const SnackBar(content: Text(Strings.offlineMessage)));
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _downloadDocument(String documentId) async {
    setState(() => _downloading = true);
    try {
      final bytes = await ref
          .read(documentActionsProvider)
          .download(documentId);
      final path = await saveBytesToDevice(bytes, 'policy-$documentId.pdf');
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('${Strings.downloadedTo} $path')),
        );
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text('$e')));
      }
    } finally {
      if (mounted) setState(() => _downloading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final detail = ref.watch(policyDetailProvider(widget.policyId));

    return AppScaffold(
      title: Strings.policies,
      body: RefreshIndicator(
        onRefresh: () async =>
            ref.invalidate(policyDetailProvider(widget.policyId)),
        child: AsyncBody(
          value: detail,
          onRetry: () => ref.invalidate(policyDetailProvider(widget.policyId)),
          builder: (policy) => SingleChildScrollView(
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
                        policy.title,
                        style: Theme.of(context).textTheme.titleLarge
                            ?.copyWith(fontWeight: FontWeight.w700),
                      ),
                    ),
                    if (policy.acknowledgedAt != null)
                      const StatusChip(status: 'Completed', dense: true)
                    else if (policy.needsAcknowledgement)
                      const StatusChip(status: 'Pending', dense: true),
                  ],
                ),
                const SizedBox(height: 16),
                SectionCard(
                  title: Strings.summary,
                  child: Column(
                    children: [
                      if (policy.category != null)
                        InfoRow(
                          label: Strings.category,
                          value: policy.category!,
                        ),
                      if (policy.version != null)
                        InfoRow(label: 'Version', value: policy.version!),
                      if (policy.publishedAt != null)
                        InfoRow(
                          label: 'Published',
                          value: AppFormatters.date(policy.publishedAt),
                        ),
                      if (policy.effectiveFrom != null)
                        InfoRow(
                          label: 'Effective from',
                          value: AppFormatters.date(policy.effectiveFrom),
                        ),
                      if (policy.acknowledgedAt != null)
                        InfoRow(
                          label: Strings.acknowledged,
                          value: AppFormatters.timestamp(policy.acknowledgedAt),
                        ),
                    ],
                  ),
                ),
                if (policy.body != null && policy.body!.isNotEmpty) ...[
                  const SizedBox(height: 12),
                  SectionCard(title: 'Policy text', child: Text(policy.body!)),
                ],
                const SizedBox(height: 20),
                if (policy.needsAcknowledgement)
                  SizedBox(
                    width: double.infinity,
                    child: AppButton(
                      label: Strings.acknowledge,
                      icon: Icons.how_to_reg_rounded,
                      busy: _busy,
                      onPressed: _busy ? null : _acknowledge,
                    ),
                  )
                else if (policy.acknowledgedAt != null)
                  Text(
                    Strings.policyAlreadyAcknowledged,
                    style: Theme.of(context).textTheme.bodySmall?.copyWith(
                      color: Theme.of(context).colorScheme.onSurfaceVariant,
                    ),
                    textAlign: TextAlign.center,
                  ),
                if (policy.documentId != null) ...[
                  const SizedBox(height: 12),
                  SizedBox(
                    width: double.infinity,
                    child: OutlinedButton.icon(
                      onPressed: _downloading
                          ? null
                          : () => _downloadDocument(policy.documentId!),
                      icon: _downloading
                          ? const SizedBox(
                              width: 18,
                              height: 18,
                              child: CircularProgressIndicator(
                                strokeWidth: 2.4,
                              ),
                            )
                          : const Icon(Icons.download_rounded, size: 18),
                      label: const Text(Strings.download),
                    ),
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
