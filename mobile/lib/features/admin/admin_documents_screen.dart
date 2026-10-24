import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/strings.dart';
import '../../models/document_models.dart';
import '../../providers/api_provider.dart';
import '../../providers/directory_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';

final adminEmployeeDocumentsProvider =
    FutureProvider.family<List<EmployeeDocument>, String>((
      ref,
      employeeId,
    ) async {
      final items = await ref
          .watch(apiClientProvider)
          .getList(
            '/documents',
            query: {'employeeId': employeeId, 'status': 'Pending'},
          );
      return items
          .whereType<Map>()
          .map(
            (item) =>
                EmployeeDocument.fromJson(Map<String, dynamic>.from(item)),
          )
          .toList(growable: false);
    });

class AdminDocumentsScreen extends ConsumerStatefulWidget {
  const AdminDocumentsScreen({super.key});

  @override
  ConsumerState<AdminDocumentsScreen> createState() =>
      _AdminDocumentsScreenState();
}

class _AdminDocumentsScreenState extends ConsumerState<AdminDocumentsScreen> {
  String? _employeeId;
  String? _busyId;

  Future<String?> _reviewNote(String action) async {
    final controller = TextEditingController();
    final note = await showDialog<String>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: Text('$action document'),
        content: TextField(
          controller: controller,
          minLines: 2,
          maxLines: 4,
          decoration: InputDecoration(
            labelText: action == 'Reject'
                ? 'Rejection reason'
                : 'Note (optional)',
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(dialogContext).pop(),
            child: const Text(Strings.cancel),
          ),
          FilledButton(
            onPressed: () {
              if (action == 'Reject' && controller.text.trim().isEmpty) return;
              Navigator.of(dialogContext).pop(controller.text.trim());
            },
            child: Text(action),
          ),
        ],
      ),
    );
    controller.dispose();
    return note;
  }

  Future<void> _decide(EmployeeDocument document, String action) async {
    final note = await _reviewNote(action);
    if (note == null || !mounted || _employeeId == null) return;
    setState(() => _busyId = document.id);
    try {
      await ref
          .read(apiClientProvider)
          .postMap(
            '/documents/${document.id}/decision',
            data: {'action': action, 'note': note},
          );
      ref.invalidate(adminEmployeeDocumentsProvider(_employeeId!));
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(
              action == 'Verify' ? 'Document verified' : 'Document rejected',
            ),
          ),
        );
      }
    } catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Document review failed: $error')),
        );
      }
    } finally {
      if (mounted) setState(() => _busyId = null);
    }
  }

  @override
  Widget build(BuildContext context) {
    final options = ref.watch(employeeCreateOptionsProvider);
    return AppScaffold(
      title: 'Document verification',
      body: AsyncBody(
        value: options,
        onRetry: () => ref.invalidate(employeeCreateOptionsProvider),
        builder: (data) => Column(
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 12, 16, 8),
              child: DropdownButtonFormField<String>(
                initialValue: _employeeId,
                decoration: const InputDecoration(
                  labelText: 'Employee',
                  prefixIcon: Icon(Icons.person_search_outlined),
                ),
                items: [
                  for (final employee in data.managers)
                    DropdownMenuItem(
                      value: employee.employeeId,
                      child: Text(employee.label),
                    ),
                ],
                onChanged: (value) => setState(() => _employeeId = value),
              ),
            ),
            Expanded(
              child: _employeeId == null
                  ? const Center(
                      child: Text(
                        'Choose an employee to review pending documents.',
                      ),
                    )
                  : _PendingDocumentsList(
                      employeeId: _employeeId!,
                      busyId: _busyId,
                      onReview: _decide,
                    ),
            ),
          ],
        ),
      ),
    );
  }
}

class _PendingDocumentsList extends ConsumerWidget {
  const _PendingDocumentsList({
    required this.employeeId,
    required this.busyId,
    required this.onReview,
  });

  final String employeeId;
  final String? busyId;
  final Future<void> Function(EmployeeDocument, String) onReview;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final documents = ref.watch(adminEmployeeDocumentsProvider(employeeId));
    return AsyncBody(
      value: documents,
      onRetry: () => ref.invalidate(adminEmployeeDocumentsProvider(employeeId)),
      isEmpty: (items) => items.isEmpty,
      emptyTitle: 'No pending documents',
      emptyMessage:
          'There are no documents awaiting verification for this employee.',
      emptyIcon: Icons.verified_outlined,
      builder: (items) => ListView.separated(
        physics: const AlwaysScrollableScrollPhysics(),
        padding: const EdgeInsets.fromLTRB(16, 4, 16, 20),
        itemCount: items.length,
        separatorBuilder: (_, __) => const SizedBox(height: 8),
        itemBuilder: (context, index) {
          final document = items[index];
          return Card(
            child: Padding(
              padding: const EdgeInsets.all(14),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    document.name,
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                  const SizedBox(height: 4),
                  Text(
                    '${document.category} · ${document.documentType ?? 'Document'}',
                  ),
                  const SizedBox(height: 12),
                  Row(
                    children: [
                      Expanded(
                        child: OutlinedButton.icon(
                          onPressed: busyId == document.id
                              ? null
                              : () => onReview(document, 'Reject'),
                          icon: const Icon(Icons.close_rounded),
                          label: const Text('Reject'),
                        ),
                      ),
                      const SizedBox(width: 10),
                      Expanded(
                        child: FilledButton.icon(
                          onPressed: busyId == document.id
                              ? null
                              : () => onReview(document, 'Verify'),
                          icon: busyId == document.id
                              ? const SizedBox.square(
                                  dimension: 16,
                                  child: CircularProgressIndicator(
                                    strokeWidth: 2,
                                  ),
                                )
                              : const Icon(Icons.verified_outlined),
                          label: const Text('Verify'),
                        ),
                      ),
                    ],
                  ),
                ],
              ),
            ),
          );
        },
      ),
    );
  }
}
