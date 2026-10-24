import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../core/network/api_client.dart';
import '../models/document_models.dart';
import 'api_provider.dart';

/// Filter for `GET /documents`.
class DocumentQuery {
  const DocumentQuery({this.category, this.status});

  final String? category;
  final String? status;

  @override
  bool operator ==(Object other) =>
      other is DocumentQuery &&
      other.category == category &&
      other.status == status;

  @override
  int get hashCode => Object.hash(category, status);
}

/// `GET /documents` (FR-EMP-070) — mine by default.
final documentsProvider =
    FutureProvider.family<List<EmployeeDocument>, DocumentQuery>((
      ref,
      query,
    ) async {
      final api = ref.watch(apiClientProvider);
      final params = <String, dynamic>{};
      if (query.category != null) params['category'] = query.category;
      if (query.status != null) params['status'] = query.status;
      final list = await api.getList('/documents', query: params);
      return list
          .whereType<Map>()
          .map((e) => EmployeeDocument.fromJson(Map<String, dynamic>.from(e)))
          .toList(growable: false);
    });

/// `GET /letters/requests` — mine.
final letterRequestsProvider = FutureProvider<List<LetterRequest>>((ref) async {
  final api = ref.watch(apiClientProvider);
  final list = await api.getList('/letters/requests');
  return list
      .whereType<Map>()
      .map((e) => LetterRequest.fromJson(Map<String, dynamic>.from(e)))
      .toList(growable: false);
});

/// `GET /policies` (FR-EMP-074).
/// Family key: acknowledgement filter (`''` = all).
final policiesProvider = FutureProvider.family<List<Policy>, String>((
  ref,
  ackFilter,
) async {
  final api = ref.watch(apiClientProvider);
  final params = <String, dynamic>{};
  if (ackFilter.isNotEmpty) params['acknowledgementStatus'] = ackFilter;
  final list = await api.getList('/policies', query: params);
  return list
      .whereType<Map>()
      .map((e) => Policy.fromJson(Map<String, dynamic>.from(e)))
      .toList(growable: false);
});

/// `GET /policies/{id}` detail incl. body.
final policyDetailProvider = FutureProvider.family<PolicyDetail, String>((
  ref,
  id,
) async {
  final api = ref.watch(apiClientProvider);
  final json = await api.getMap('/policies/$id');
  return PolicyDetail.fromJson(json);
});

/// Document/letter/policy actions (uploads are optional in the UI).
class DocumentActions {
  DocumentActions(this._api);

  final ApiClient _api;

  /// `POST /documents` — multipart: file + category + documentType.
  Future<EmployeeDocument> uploadDocument({
    required String filePath,
    required String fileName,
    required String category,
    required String documentType,
    String? issuedOn,
    String? expiresOn,
  }) async {
    final formData = FormData.fromMap({
      'file': await MultipartFile.fromFile(filePath, filename: fileName),
      'category': category,
      'documentType': documentType,
      if (issuedOn != null && issuedOn.isNotEmpty) 'issuedOn': issuedOn,
      if (expiresOn != null && expiresOn.isNotEmpty) 'expiresOn': expiresOn,
    });
    final json = await _api.postMultipart('/documents', formData: formData);
    return EmployeeDocument.fromJson(json);
  }

  /// `POST /letters/requests` -> 201 (FR-EMP-071/072).
  Future<LetterRequest> requestLetter({
    required String letterType,
    required String purpose,
    String? notes,
  }) async {
    final json = await _api.postMap(
      '/letters/requests',
      data: {
        'letterType': letterType,
        'purpose': purpose,
        if (notes != null && notes.isNotEmpty) 'notes': notes,
      },
    );
    return LetterRequest.fromJson(json);
  }

  /// `POST /policies/{id}/acknowledge` (FR-EMP-074, versioned).
  Future<void> acknowledgePolicy({required String id, String? comment}) async {
    await _api.postMap(
      '/policies/$id/acknowledge',
      data: comment == null ? <String, dynamic>{} : {'comment': comment},
    );
  }

  /// Downloads file bytes from an endpoint returning file content.
  Future<List<int>> download(String path) => _api.downloadBytes(path);

  /// Letter PDF: `GET /letters/requests/{id}/download` (409 before approval).
  Future<List<int>> downloadLetter(String id) =>
      _api.downloadBytes('/letters/requests/$id/download');

  /// Employee document: `GET /documents/{id}/download`.
  Future<List<int>> downloadDocument(String id) =>
      _api.downloadBytes('/documents/$id/download');
}

final documentActionsProvider = Provider<DocumentActions>(
  (ref) => DocumentActions(ref.watch(apiClientProvider)),
);
