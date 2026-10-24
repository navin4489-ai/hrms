import 'common.dart';

/// `GET /cases/categories` item.
class CaseCategory {
  const CaseCategory({
    required this.id,
    required this.name,
    this.confidential = false,
    this.subcategories = const [],
  });

  factory CaseCategory.fromJson(Map<String, dynamic> json) => CaseCategory(
    id: JsonX.strOr(json['id']),
    name: JsonX.strOr(json['name']),
    confidential: JsonX.boolOr(json['confidential']),
    subcategories: JsonX.mapList(json['subcategories'])
        .map(CaseSubcategory.fromJson)
        .toList(growable: false),
  );

  final String id;
  final String name;
  final bool confidential;
  final List<CaseSubcategory> subcategories;
}

/// One subcategory under a case category.
class CaseSubcategory {
  const CaseSubcategory({required this.id, required this.name});

  factory CaseSubcategory.fromJson(Map<String, dynamic> json) =>
      CaseSubcategory(
        id: JsonX.strOr(json['id']),
        name: JsonX.strOr(json['name']),
      );

  final String id;
  final String name;
}

/// `GET /cases` item.
class CaseItem {
  const CaseItem({
    required this.id,
    required this.ticketNumber,
    required this.subject,
    this.category,
    this.subcategory,
    this.priority = 'Medium',
    this.status = 'Open',
    this.queue,
    this.confidential = false,
    this.createdAt,
    this.updatedAt,
    this.slaDueAt,
    this.firstResponseAt,
    this.resolvedAt,
    this.assigneeName,
    this.employeeName,
    this.rating,
  });

  factory CaseItem.fromJson(Map<String, dynamic> json) => CaseItem(
    id: JsonX.strOr(json['id']),
    ticketNumber: JsonX.strOr(json['ticketNumber']),
    subject: JsonX.strOr(json['subject']),
    category: JsonX.str(json['category']),
    subcategory: JsonX.str(json['subcategory']),
    priority: JsonX.strOr(json['priority'], 'Medium'),
    status: JsonX.strOr(json['status'], 'Open'),
    queue: JsonX.str(json['queue']),
    confidential: JsonX.boolOr(json['confidential']),
    createdAt: JsonX.str(json['createdAt']),
    updatedAt: JsonX.str(json['updatedAt']),
    slaDueAt: JsonX.str(json['slaDueAt']),
    firstResponseAt: JsonX.str(json['firstResponseAt']),
    resolvedAt: JsonX.str(json['resolvedAt']),
    assigneeName: JsonX.str(json['assigneeName']),
    employeeName: JsonX.str(json['employeeName']),
    rating: JsonX.intOrNull(json['rating']),
  );

  final String id;
  final String ticketNumber;
  final String subject;
  final String? category;
  final String? subcategory;
  final String priority;
  final String status;
  final String? queue;
  final bool confidential;
  final String? createdAt;
  final String? updatedAt;
  final String? slaDueAt;
  final String? firstResponseAt;
  final String? resolvedAt;
  final String? assigneeName;
  final String? employeeName;
  final int? rating;
}

/// One message in `GET /cases/{id}.messages`.
/// Internal notes are omitted by the API for employees (FR-ADM-122);
/// the model filters defensively anyway.
class CaseMessage {
  const CaseMessage({
    required this.id,
    required this.senderName,
    this.senderRole,
    this.visibility = 'Employee',
    required this.body,
    this.createdAt,
    this.attachmentUrl,
  });

  factory CaseMessage.fromJson(Map<String, dynamic> json) => CaseMessage(
    id: JsonX.strOr(json['id']),
    senderName: JsonX.strOr(json['senderName']),
    senderRole: JsonX.str(json['senderRole']),
    visibility: JsonX.strOr(json['visibility'], 'Employee'),
    body: JsonX.strOr(json['body']),
    createdAt: JsonX.str(json['createdAt']),
    attachmentUrl: JsonX.str(json['attachmentUrl']),
  );

  final String id;
  final String senderName;
  final String? senderRole;
  final String visibility;
  final String body;
  final String? createdAt;
  final String? attachmentUrl;

  bool get isInternal => visibility != 'Employee';
}

/// `GET /cases/{id}` detail (adds messages + canReply).
class CaseDetail extends CaseItem {
  const CaseDetail({
    required super.id,
    required super.ticketNumber,
    required super.subject,
    super.category,
    super.subcategory,
    super.priority,
    super.status,
    super.queue,
    super.confidential,
    super.createdAt,
    super.updatedAt,
    super.slaDueAt,
    super.firstResponseAt,
    super.resolvedAt,
    super.assigneeName,
    super.employeeName,
    super.rating,
    this.description,
    this.messages = const [],
    this.canReply = false,
  });

  factory CaseDetail.fromJson(Map<String, dynamic> json) => CaseDetail(
    id: JsonX.strOr(json['id']),
    ticketNumber: JsonX.strOr(json['ticketNumber']),
    subject: JsonX.strOr(json['subject']),
    category: JsonX.str(json['category']),
    subcategory: JsonX.str(json['subcategory']),
    priority: JsonX.strOr(json['priority'], 'Medium'),
    status: JsonX.strOr(json['status'], 'Open'),
    queue: JsonX.str(json['queue']),
    confidential: JsonX.boolOr(json['confidential']),
    createdAt: JsonX.str(json['createdAt']),
    updatedAt: JsonX.str(json['updatedAt']),
    slaDueAt: JsonX.str(json['slaDueAt']),
    firstResponseAt: JsonX.str(json['firstResponseAt']),
    resolvedAt: JsonX.str(json['resolvedAt']),
    assigneeName: JsonX.str(json['assigneeName']),
    employeeName: JsonX.str(json['employeeName']),
    rating: JsonX.intOrNull(json['rating']),
    description: JsonX.str(json['description']),
    messages: JsonX.mapList(json['messages'])
        .map(CaseMessage.fromJson)
        .where((m) => !m.isInternal)
        .toList(growable: false),
    canReply: JsonX.boolOr(json['canReply']),
  );

  final String? description;
  final List<CaseMessage> messages;
  final bool canReply;

  bool get canReopen => status == 'Resolved';
  bool get canRate => status == 'Resolved' && rating == null;
  bool get isClosed => status == 'Closed';
}

/// `GET /knowledge-base` item.
class KbArticle {
  const KbArticle({
    required this.id,
    required this.title,
    this.summary,
    this.category,
    this.tags = const [],
    this.helpfulCount = 0,
    this.updatedAt,
  });

  factory KbArticle.fromJson(Map<String, dynamic> json) => KbArticle(
    id: JsonX.strOr(json['id']),
    title: JsonX.strOr(json['title']),
    summary: JsonX.str(json['summary']),
    category: JsonX.str(json['category']),
    tags: JsonX.stringList(json['tags']),
    helpfulCount: JsonX.intOrNull(json['helpfulCount']) ?? 0,
    updatedAt: JsonX.str(json['updatedAt']),
  );

  final String id;
  final String title;
  final String? summary;
  final String? category;
  final List<String> tags;
  final int helpfulCount;
  final String? updatedAt;
}

/// `GET /knowledge-base/{id}` detail.
class KbArticleDetail {
  const KbArticleDetail({
    required this.id,
    required this.title,
    this.body,
    this.category,
    this.version,
    this.expiresAt,
  });

  factory KbArticleDetail.fromJson(Map<String, dynamic> json) =>
      KbArticleDetail(
        id: JsonX.strOr(json['id']),
        title: JsonX.strOr(json['title']),
        body: JsonX.str(json['body']),
        category: JsonX.str(json['category']),
        version: JsonX.str(json['version']),
        expiresAt: JsonX.str(json['expiresAt']),
      );

  final String id;
  final String title;
  final String? body;
  final String? category;
  final String? version;
  final String? expiresAt;
}
