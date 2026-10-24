import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/network/api_exception.dart';
import '../../core/strings.dart';
import '../../core/utils/formatters.dart';
import '../../models/asset_models.dart';
import '../../providers/assets_provider.dart';
import '../../widgets/app_button.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';
import '../../widgets/section_card.dart';
import '../../widgets/status_chip.dart';

/// My assets (FR-EMP-090): issued equipment with acknowledge-receipt
/// (FR-EMP-091) and return-request (FR-EMP-093) actions.
class AssetsScreen extends ConsumerStatefulWidget {
  const AssetsScreen({super.key});

  @override
  ConsumerState<AssetsScreen> createState() => _AssetsScreenState();
}

class _AssetsScreenState extends ConsumerState<AssetsScreen> {
  static const _conditions = <String, String>{
    'Good': 'Good',
    'Fair': 'Fair',
    'Poor': 'Poor',
    'Damaged': 'Damaged',
  };

  String? _busyId;

  Future<void> _acknowledgeSheet(Asset asset) async {
    var condition = _conditions.keys.first;
    var note = '';
    String? error;
    var busy = false;

    await showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (sheetContext) => StatefulBuilder(
        builder: (sheetContext, setSheetState) {
          Future<void> submit() async {
            setSheetState(() {
              busy = true;
              error = null;
            });
            setState(() => _busyId = asset.id);
            try {
              await ref
                  .read(assetActionsProvider)
                  .acknowledge(
                    id: asset.id,
                    condition: condition,
                    note: note.trim().isEmpty ? null : note.trim(),
                  );
              ref.invalidate(myAssetsProvider);
              if (!mounted) return;
              Navigator.of(sheetContext).pop();
              ScaffoldMessenger.of(context).showSnackBar(
                const SnackBar(content: Text(Strings.assetAcknowledged)),
              );
            } on ApiException catch (e) {
              setSheetState(() {
                busy = false;
                error = e.message;
              });
              setState(() => _busyId = null);
            } catch (_) {
              setSheetState(() {
                busy = false;
                error = Strings.offlineMessage;
              });
              setState(() => _busyId = null);
            }
          }

          return Padding(
            padding: EdgeInsets.only(
              left: 20,
              right: 20,
              bottom: MediaQuery.viewInsetsOf(sheetContext).bottom + 24,
            ),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Text(
                  Strings.acknowledgeAsset,
                  style: Theme.of(sheetContext).textTheme.titleLarge
                      ?.copyWith(fontWeight: FontWeight.w700),
                ),
                const SizedBox(height: 6),
                Text(
                  asset.name,
                  style: Theme.of(sheetContext).textTheme.bodyMedium,
                ),
                const SizedBox(height: 16),
                if (error != null) ...[
                  Container(
                    padding: const EdgeInsets.all(10),
                    decoration: BoxDecoration(
                      color: Theme.of(sheetContext).colorScheme.errorContainer,
                      borderRadius: BorderRadius.circular(10),
                    ),
                    child: Text(
                      error!,
                      style: TextStyle(
                        color: Theme.of(sheetContext)
                            .colorScheme
                            .onErrorContainer,
                        fontSize: 13.5,
                      ),
                    ),
                  ),
                  const SizedBox(height: 12),
                ],
                DropdownButtonFormField<String>(
                  initialValue: condition,
                  decoration: const InputDecoration(
                    labelText: Strings.condition,
                  ),
                  items: [
                    for (final entry in _conditions.entries)
                      DropdownMenuItem(
                        value: entry.key,
                        child: Text(entry.value),
                      ),
                  ],
                  onChanged: busy
                      ? null
                      : (value) {
                          if (value != null) {
                            setSheetState(() => condition = value);
                          }
                        },
                ),
                const SizedBox(height: 14),
                TextField(
                  enabled: !busy,
                  maxLines: 2,
                  decoration: const InputDecoration(
                    labelText: '${Strings.conditionNote} (optional)',
                  ),
                  onChanged: (value) => note = value,
                ),
                const SizedBox(height: 20),
                AppButton(
                  label: Strings.acknowledgeAsset,
                  busy: busy,
                  onPressed: busy ? null : submit,
                ),
              ],
            ),
          );
        },
      ),
    );
    if (mounted) setState(() => _busyId = null);
  }

  Future<void> _returnSheet(Asset asset) async {
    var condition = _conditions.keys.first;
    var reason = '';
    String? error;
    var busy = false;

    await showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (sheetContext) => StatefulBuilder(
        builder: (sheetContext, setSheetState) {
          Future<void> submit() async {
            if (reason.trim().length < 5) {
              setSheetState(() => error = Strings.reasonRequired);
              return;
            }
            setSheetState(() {
              busy = true;
              error = null;
            });
            setState(() => _busyId = asset.id);
            try {
              await ref
                  .read(assetActionsProvider)
                  .requestReturn(
                    id: asset.id,
                    reason: reason.trim(),
                    condition: condition,
                  );
              ref.invalidate(myAssetsProvider);
              if (!mounted) return;
              Navigator.of(sheetContext).pop();
              ScaffoldMessenger.of(context).showSnackBar(
                const SnackBar(content: Text(Strings.assetReturnRequested)),
              );
            } on ApiException catch (e) {
              setSheetState(() {
                busy = false;
                error = e.message;
              });
              setState(() => _busyId = null);
            } catch (_) {
              setSheetState(() {
                busy = false;
                error = Strings.offlineMessage;
              });
              setState(() => _busyId = null);
            }
          }

          return Padding(
            padding: EdgeInsets.only(
              left: 20,
              right: 20,
              bottom: MediaQuery.viewInsetsOf(sheetContext).bottom + 24,
            ),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Text(
                  Strings.requestAssetReturn,
                  style: Theme.of(sheetContext).textTheme.titleLarge
                      ?.copyWith(fontWeight: FontWeight.w700),
                ),
                const SizedBox(height: 6),
                Text(
                  asset.name,
                  style: Theme.of(sheetContext).textTheme.bodyMedium,
                ),
                const SizedBox(height: 16),
                if (error != null) ...[
                  Container(
                    padding: const EdgeInsets.all(10),
                    decoration: BoxDecoration(
                      color: Theme.of(sheetContext).colorScheme.errorContainer,
                      borderRadius: BorderRadius.circular(10),
                    ),
                    child: Text(
                      error!,
                      style: TextStyle(
                        color: Theme.of(sheetContext)
                            .colorScheme
                            .onErrorContainer,
                        fontSize: 13.5,
                      ),
                    ),
                  ),
                  const SizedBox(height: 12),
                ],
                DropdownButtonFormField<String>(
                  initialValue: condition,
                  decoration: const InputDecoration(
                    labelText: Strings.condition,
                  ),
                  items: [
                    for (final entry in _conditions.entries)
                      DropdownMenuItem(
                        value: entry.key,
                        child: Text(entry.value),
                      ),
                  ],
                  onChanged: busy
                      ? null
                      : (value) {
                          if (value != null) {
                            setSheetState(() => condition = value);
                          }
                        },
                ),
                const SizedBox(height: 14),
                TextField(
                  enabled: !busy,
                  maxLines: 3,
                  decoration: const InputDecoration(
                    labelText: Strings.returnReason,
                    hintText: 'Why are you returning this asset?',
                  ),
                  onChanged: (value) => reason = value,
                ),
                const SizedBox(height: 20),
                AppButton(
                  label: Strings.requestAssetReturn,
                  busy: busy,
                  onPressed: busy ? null : submit,
                ),
              ],
            ),
          );
        },
      ),
    );
    if (mounted) setState(() => _busyId = null);
  }

  @override
  Widget build(BuildContext context) {
    final assets = ref.watch(myAssetsProvider);

    return AppScaffold(
      title: Strings.assets,
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(myAssetsProvider),
        child: AsyncBody(
          value: assets,
          onRetry: () => ref.invalidate(myAssetsProvider),
          isEmpty: (items) => items.isEmpty,
          emptyTitle: Strings.noAssets,
          emptyMessage: Strings.noAssetsHint,
          emptyIcon: Icons.laptop_mac_outlined,
          builder: (items) => ListView.separated(
            physics: const AlwaysScrollableScrollPhysics(),
            padding: const EdgeInsets.all(16),
            itemCount: items.length,
            separatorBuilder: (_, __) => const SizedBox(height: 12),
            itemBuilder: (context, index) {
              final asset = items[index];
              return SectionCard(
                title: asset.name,
                trailing: StatusChip(status: asset.status, dense: true),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    InfoRow(
                      label: 'Type',
                      value: AppFormatters.enumLabel(asset.assetType),
                    ),
                    if (asset.assetTag != null)
                      InfoRow(label: Strings.assetTag, value: asset.assetTag!),
                    if (asset.serialNumber != null)
                      InfoRow(
                        label: Strings.serialNumber,
                        value: asset.serialNumber!,
                      ),
                    if (asset.model != null)
                      InfoRow(label: 'Model', value: asset.model!),
                    InfoRow(
                      label: Strings.issuedOnLabel,
                      value: AppFormatters.date(asset.issuedAt),
                    ),
                    if (asset.condition != null && asset.condition!.isNotEmpty)
                      InfoRow(
                        label: Strings.condition,
                        value: asset.condition!,
                      ),
                    if (asset.returnDueDate != null)
                      InfoRow(
                        label: Strings.returnDueDate,
                        value: AppFormatters.date(asset.returnDueDate),
                      ),
                    const SizedBox(height: 8),
                    Wrap(
                      spacing: 10,
                      runSpacing: 8,
                      children: [
                        if (asset.canAcknowledge)
                          FilledButton.tonalIcon(
                            onPressed: _busyId != null
                                ? null
                                : () => _acknowledgeSheet(asset),
                            icon: const Icon(
                              Icons.how_to_reg_rounded,
                              size: 18,
                            ),
                            label: const Text(Strings.acknowledgeAsset),
                          ),
                        if (asset.canRequestReturn)
                          OutlinedButton.icon(
                            onPressed: _busyId != null
                                ? null
                                : () => _returnSheet(asset),
                            icon: const Icon(
                              Icons.assignment_returned_outlined,
                              size: 18,
                            ),
                            label: const Text(Strings.requestAssetReturn),
                          ),
                      ],
                    ),
                  ],
                ),
              );
            },
          ),
        ),
      ),
    );
  }
}
