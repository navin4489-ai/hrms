import 'package:file_picker/file_picker.dart';
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

/// My documents (FR-EMP-070): category filter, status, download and upload
/// (multipart, allow-listed types, server-side verification).
class DocumentsScreen extends ConsumerStatefulWidget {
  const DocumentsScreen({super.key});

  @override
  ConsumerState<DocumentsScreen> createState() => _DocumentsScreenState();
}

class _DocumentsScreenState extends ConsumerState<DocumentsScreen> {
  static const _categories = <String>[
    'Identity',
    'Education',
    'Employment',
    'Tax',
    'Medical',
    'Other',
  ];

  String? _category;
  String? _downloadingId;

  Future<void> _download(String id, String name) async {
    setState(() => _downloadingId = id);
    try {
      final bytes = await ref
          .read(documentActionsProvider)
          .downloadDocument(id);
      final path = await saveBytesToDevice(
        bytes,
        name.contains('.') ? name : '$name.pdf',
      );
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

  Future<void> _uploadSheet() async {
    String? filePath;
    String? fileName;
    var category = _categories.first;
    var documentType = '';
    String? error;
    var busy = false;

    await showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (sheetContext) => StatefulBuilder(
        builder: (sheetContext, setSheetState) {
          Future<void> submit() async {
            if (filePath == null) {
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
                  .uploadDocument(
                    filePath: filePath!,
                    fileName: fileName ?? 'document',
                    category: category,
                    documentType: documentType.trim(),
                  );
              ref.invalidate(documentsProvider);
              if (!mounted) return;
              Navigator.of(sheetContext).pop();
              ScaffoldMessenger.of(context).showSnackBar(
                const SnackBar(
                  content: Text('Document uploaded for verification'),
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
                    Strings.uploadDocument,
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
                  OutlinedButton.icon(
                    onPressed: busy
                        ? null
                        : () async {
                            final result = await FilePicker.pickFiles(
                              type: FileType.any,
                            );
                            if (result.isEmpty || result.first.path == null) {
                              return;
                            }
                            setSheetState(() {
                              filePath = result.first.path;
                              fileName = result.first.name;
                              error = null;
                            });
                          },
                    icon: const Icon(Icons.attach_file_rounded, size: 18),
                    label: Text(
                      fileName ?? Strings.chooseFile,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                    ),
                  ),
                  const SizedBox(height: 14),
                  DropdownButtonFormField<String>(
                    initialValue: category,
                    decoration: const InputDecoration(
                      labelText: Strings.category,
                    ),
                    items: [
                      for (final value in _categories)
                        DropdownMenuItem(value: value, child: Text(value)),
                    ],
                    onChanged: busy
                        ? null
                        : (value) {
                            if (value != null) {
                              setSheetState(() => category = value);
                            }
                          },
                  ),
                  const SizedBox(height: 14),
                  TextField(
                    enabled: !busy,
                    decoration: const InputDecoration(
                      labelText: Strings.documentType,
                      hintText: 'e.g. Passport, Offer letter',
                    ),
                    onChanged: (value) => documentType = value,
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
    final query = DocumentQuery(category: _category);
    final documents = ref.watch(documentsProvider(query));

    return AppScaffold(
      title: Strings.documents,
      floatingActionButton: FloatingActionButton.extended(
        onPressed: _uploadSheet,
        icon: const Icon(Icons.upload_rounded),
        label: const Text(Strings.uploadDocument),
      ),
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(documentsProvider(query)),
        child: Column(
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 12, 16, 4),
              child: SingleChildScrollView(
                scrollDirection: Axis.horizontal,
                child: Row(
                  children: [
                    for (final value in ['All', ..._categories])
                      Padding(
                        padding: const EdgeInsets.only(right: 8),
                        child: ChoiceChip(
                          label: Text(value),
                          selected:
                              (value == 'All' && _category == null) ||
                              value == _category,
                          onSelected: (_) => setState(
                            () => _category = value == 'All' ? null : value,
                          ),
                        ),
                      ),
                  ],
                ),
              ),
            ),
            Expanded(
              child: AsyncBody(
                value: documents,
                onRetry: () => ref.invalidate(documentsProvider(query)),
                isEmpty: (items) => items.isEmpty,
                emptyTitle: Strings.noDocuments,
                emptyMessage:
                    'Documents you upload appear here after verification.',
                emptyIcon: Icons.folder_copy_outlined,
                builder: (items) => ListView.separated(
                  physics: const AlwaysScrollableScrollPhysics(),
                  padding: const EdgeInsets.fromLTRB(16, 8, 16, 88),
                  itemCount: items.length,
                  separatorBuilder: (_, __) => const SizedBox(height: 10),
                  itemBuilder: (context, index) {
                    final document = items[index];
                    return Card(
                      clipBehavior: Clip.antiAlias,
                      child: ListTile(
                        leading: const Icon(Icons.description_outlined),
                        title: Text(document.name),
                        subtitle: Text(
                          [
                            document.category,
                            if (document.expiresOn != null)
                              '${Strings.expiresOn} '
                                  '${AppFormatters.date(document.expiresOn)}',
                            if (document.sizeBytes != null)
                              AppFormatters.fileSize(document.sizeBytes),
                          ].join(' · '),
                        ),
                        trailing: Row(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            StatusChip(status: document.status, dense: true),
                            if (document.canDownload)
                              _downloadingId == document.id
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
                                      onPressed: () =>
                                          _download(document.id, document.name),
                                    ),
                          ],
                        ),
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
