import 'common.dart';

/// `GET /documents` item.
class EmployeeDocument {
  const EmployeeDocument({
    required this.id,
    required this.name,
    required this.category,
    this.documentType,
    this.issuedOn,
    this.expiresOn,
    required this.status,
    this.verificationNote,
    this.sizeBytes,
    this.fileExtension,
    this.canDownload = false,
    this.uploadedAt,
  });

  factory EmployeeDocument.fromJson(Map<String, dynamic> json) =>
      EmployeeDocument(
        id: JsonX.strOr(json['id']),
        name: JsonX.strOr(json['name']),
        category: JsonX.strOr(json['category']),
        documentType: JsonX.str(json['documentType']),
        issuedOn: JsonX.str(json['issuedOn']),
        expiresOn: JsonX.str(json['expiresOn']),
        status: JsonX.strOr(json['status'], 'Pending'),
        verificationNote: JsonX.str(json['verificationNote']),
        sizeBytes: JsonX.intOrNull(json['sizeBytes']),
        fileExtension: JsonX.str(json['fileExtension']),
        canDownload: JsonX.boolOr(json['canDownload']),
        uploadedAt: JsonX.str(json['uploadedAt']),
      );

  final String id;
  final String name;
  final String category;
  final String? documentType;
  final String? issuedOn;
  final String? expiresOn;
  final String status;
  final String? verificationNote;
  final int? sizeBytes;
  final String? fileExtension;
  final bool canDownload;
  final String? uploadedAt;
}

/// `GET /letters/requests` item.
class LetterRequest {
  const LetterRequest({
    required this.id,
    required this.letterType,
    this.purpose,
    required this.status,
    this.createdAt,
    this.approvalTaskId,
    this.generatedDocumentId,
  });

  factory LetterRequest.fromJson(Map<String, dynamic> json) => LetterRequest(
    id: JsonX.strOr(json['id']),
    letterType: JsonX.strOr(json['letterType']),
    purpose: JsonX.str(json['purpose']),
    status: JsonX.strOr(json['status'], 'Pending'),
    createdAt: JsonX.str(json['createdAt']),
    approvalTaskId: JsonX.str(json['approvalTaskId']),
    generatedDocumentId: JsonX.str(json['generatedDocumentId']),
  );

  final String id;
  final String letterType;
  final String? purpose;
  final String status;
  final String? createdAt;
  final String? approvalTaskId;
  final String? generatedDocumentId;

  bool get canDownload => generatedDocumentId != null;
}

/// `GET /policies` item.
class Policy {
  const Policy({
    required this.id,
    required this.title,
    this.category,
    this.version,
    this.publishedAt,
    this.effectiveFrom,
    this.requiresAcknowledgement = false,
    this.acknowledgedAt,
    this.documentId,
  });

  factory Policy.fromJson(Map<String, dynamic> json) => Policy(
    id: JsonX.strOr(json['id']),
    title: JsonX.strOr(json['title']),
    category: JsonX.str(json['category']),
    version: JsonX.str(json['version']),
    publishedAt: JsonX.str(json['publishedAt']),
    effectiveFrom: JsonX.str(json['effectiveFrom']),
    requiresAcknowledgement: JsonX.boolOr(json['requiresAcknowledgement']),
    acknowledgedAt: JsonX.str(json['acknowledgedAt']),
    documentId: JsonX.str(json['documentId']),
  );

  final String id;
  final String title;
  final String? category;
  final String? version;
  final String? publishedAt;
  final String? effectiveFrom;
  final bool requiresAcknowledgement;
  final String? acknowledgedAt;
  final String? documentId;

  bool get needsAcknowledgement =>
      requiresAcknowledgement && acknowledgedAt == null;
}

/// `GET /policies/{id}` detail (adds short body text).
class PolicyDetail extends Policy {
  const PolicyDetail({
    required super.id,
    required super.title,
    super.category,
    super.version,
    super.publishedAt,
    super.effectiveFrom,
    super.requiresAcknowledgement,
    super.acknowledgedAt,
    super.documentId,
    this.body,
  });

  factory PolicyDetail.fromJson(Map<String, dynamic> json) => PolicyDetail(
    id: JsonX.strOr(json['id']),
    title: JsonX.strOr(json['title']),
    category: JsonX.str(json['category']),
    version: JsonX.str(json['version']),
    publishedAt: JsonX.str(json['publishedAt']),
    effectiveFrom: JsonX.str(json['effectiveFrom']),
    requiresAcknowledgement: JsonX.boolOr(json['requiresAcknowledgement']),
    acknowledgedAt: JsonX.str(json['acknowledgedAt']),
    documentId: JsonX.str(json['documentId']),
    body: JsonX.str(json['body']),
  );

  final String? body;
}
