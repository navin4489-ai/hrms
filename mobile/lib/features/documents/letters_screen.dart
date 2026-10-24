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
import '../../widgets/status_chip.dart';

/// Letter requests (FR-EMP-071/072): employment / salary / experience /
/// address letters routed through approvals; PDF download once generated.
class LettersScreen extends ConsumerStatefulWidget {
  const LettersScreen({super.key});

  @override
  ConsumerState<LettersScreen> createState() => _LettersScreenState();
}

class _LettersScreenState extends ConsumerState<LettersScreen> {
  String? _downloadingId;

  static const _letterTypes = <String, String>{
    'EmploymentCertificate': 'Employment certificate',
    'SalaryCertificate': 'Salary certificate',
    'ExperienceLetter': 'Experience letter',
    'AddressProof': 'Address proof',
  };

  Future<void> _download(String id) async {
    setState(() => _downloadingId = id);
    try {
      final bytes = await ref.read(documentActionsProvider).downloadLetter(id);
      final path = await saveBytesToDevice(bytes, 'letter-$id.pdf');
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
      if (mounted) setState(() => _downloadingId = null);
    }
  }

  Future<void> _requestSheet() async {
    var letterType = _letterTypes.keys.first;
    var purpose = '';
    var notes = '';
    String? error;
    var busy = false;

    await showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (sheetContext) => StatefulBuilder(
        builder: (sheetContext, setSheetState) {
          Future<void> submit() async {
            if (purpose.trim().isEmpty) {
              setSheetState(() => error = Strings.fieldRequired);
              return;
            }
            setSheetState(() {
              busy = true;
              error = null;
            });
            try {
              await ref
                  .read(documentActionsProvider)
                  .requestLetter(
                    letterType: letterType,
                    purpose: purpose.trim(),
                    notes: notes.trim().isEmpty ? null : notes.trim(),
                  );
              ref.invalidate(letterRequestsProvider);
              if (!mounted) return;
              Navigator.of(sheetContext).pop();
              ScaffoldMessenger.of(context).showSnackBar(
                const SnackBar(
                  content: Text('Letter request submitted for approval'),
                ),
              );
            } on ApiException catch (e) {
              setSheetState(() {
                busy = false;
                error = e.message;
              });
            } catch (_) {
              setSheetState(() {
                busy = false;
                error = Strings.offlineMessage;
              });
            }
          }

          return Padding(
            padding: EdgeInsets.only(
              left: 20,
              right: 20,
              bottom: MediaQuery.viewInsetsOf(sheetContext).bottom + 24,
            ),
            child: SingleChildScrollView(
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Text(
                    Strings.requestLetter,
                    style: Theme.of(sheetContext).textTheme.titleLarge
                        ?.copyWith(fontWeight: FontWeight.w700),
                  ),
                  const SizedBox(height: 16),
                  if (error != null) ...[
                    Container(
                      padding: const EdgeInsets.all(10),
                      decoration: BoxDecoration(
                        color: Theme.of(sheetContext)
                            .colorScheme
                            .errorContainer,
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
                    initialValue: letterType,
                    decoration: const InputDecoration(
                      labelText: Strings.letterType,
                    ),
                    items: [
                      for (final entry in _letterTypes.entries)
                        DropdownMenuItem(
                          value: entry.key,
                          child: Text(entry.value),
                        ),
                    ],
                    onChanged: busy
                        ? null
                        : (value) {
                            if (value != null) {
                              setSheetState(() => letterType = value);
                            }
                          },
                  ),
                  const SizedBox(height: 14),
                  TextField(
                    enabled: !busy,
                    maxLines: 2,
                    decoration: const InputDecoration(
                      labelText: Strings.purpose,
                      hintText: 'e.g. Visa application',
                    ),
                    onChanged: (value) => purpose = value,
                  ),
                  const SizedBox(height: 14),
                  TextField(
                    enabled: !busy,
                    maxLines: 2,
                    decoration: const InputDecoration(
                      labelText: 'Notes (optional)',
                    ),
                    onChanged: (value) => notes = value,
                  ),
                  const SizedBox(height: 20),
                  AppButton(
                    label: Strings.submit,
                    busy: busy,
                    onPressed: busy ? null : submit,
                  ),
                ],
              ),
            ),
          );
        },
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final letters = ref.watch(letterRequestsProvider);

    return AppScaffold(
      title: Strings.letters,
      floatingActionButton: FloatingActionButton.extended(
        onPressed: _requestSheet,
        icon: const Icon(Icons.add_rounded),
        label: const Text(Strings.requestLetter),
      ),
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(letterRequestsProvider),
        child: AsyncBody(
          value: letters,
          onRetry: () => ref.invalidate(letterRequestsProvider),
          isEmpty: (items) => items.isEmpty,
          emptyTitle: Strings.noLetters,
          emptyMessage: Strings.noLettersHint,
          emptyIcon: Icons.description_outlined,
          builder: (items) => ListView.separated(
            physics: const AlwaysScrollableScrollPhysics(),
            padding: const EdgeInsets.fromLTRB(16, 16, 16, 88),
            itemCount: items.length,
            separatorBuilder: (_, __) => const SizedBox(height: 10),
            itemBuilder: (context, index) {
              final letter = items[index];
              return Card(
                clipBehavior: Clip.antiAlias,
                child: ListTile(
                  leading: const Icon(Icons.description_outlined),
                  title: Text(
                    _letterTypes[letter.letterType] ?? letter.letterType,
                  ),
                  subtitle: Text(
                    [
                      if (letter.purpose != null && letter.purpose!.isNotEmpty)
                        letter.purpose!,
                      'Requested ${AppFormatters.date(letter.createdAt)}',
                    ].join(' · '),
                  ),
                  trailing: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      StatusChip(status: letter.status, dense: true),
                      if (letter.canDownload)
                        _downloadingId == letter.id
                            ? const Padding(
                                padding: EdgeInsets.all(8),
                                child: SizedBox(
                                  width: 16,
                                  height: 16,
                                  child: CircularProgressIndicator(
                                    strokeWidth: 2,
                                  ),
                                ),
                              )
                            : IconButton(
                                tooltip: Strings.download,
                                icon: const Icon(
                                  Icons.download_rounded,
                                  size: 20,
                                ),
                                onPressed: () => _download(letter.id),
                              ),
                    ],
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
