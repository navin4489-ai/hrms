import 'common.dart';

/// `GET /announcements` item.
class Announcement {
  const Announcement({
    required this.id,
    required this.title,
    this.summary,
    this.body,
    this.category,
    this.audience,
    this.pinned = false,
    this.publishedAt,
    this.expiresAt,
    this.requiresAcknowledgement = false,
    this.acknowledgedAt,
  });

  factory Announcement.fromJson(Map<String, dynamic> json) => Announcement(
    id: JsonX.strOr(json['id']),
    title: JsonX.strOr(json['title']),
    summary: JsonX.str(json['summary']),
    body: JsonX.str(json['body']),
    category: JsonX.str(json['category']),
    audience: JsonX.str(json['audience']),
    pinned: JsonX.boolOr(json['pinned']),
    publishedAt: JsonX.str(json['publishedAt']),
    expiresAt: JsonX.str(json['expiresAt']),
    requiresAcknowledgement: JsonX.boolOr(json['requiresAcknowledgement']),
    acknowledgedAt: JsonX.str(json['acknowledgedAt']),
  );

  final String id;
  final String title;
  final String? summary;
  final String? body;
  final String? category;
  final String? audience;
  final bool pinned;
  final String? publishedAt;
  final String? expiresAt;
  final bool requiresAcknowledgement;
  final String? acknowledgedAt;

  bool get needsAcknowledgement =>
      requiresAcknowledgement && acknowledgedAt == null;
}

/// `GET /notifications` item (FR-EMP-122).
class NotificationItem {
  const NotificationItem({
    required this.id,
    required this.title,
    this.type,
    this.body,
    this.deepLink,
    this.category,
    this.createdAt,
    this.readAt,
  });

  factory NotificationItem.fromJson(Map<String, dynamic> json) =>
      NotificationItem(
        id: JsonX.strOr(json['id']),
        title: JsonX.strOr(json['title']),
        type: JsonX.str(json['type']),
        body: JsonX.str(json['body']),
        deepLink: JsonX.str(json['deepLink']),
        category: JsonX.str(json['category']),
        createdAt: JsonX.str(json['createdAt']),
        readAt: JsonX.str(json['readAt']),
      );

  final String id;
  final String title;
  final String? type;
  final String? body;
  final String? deepLink;
  final String? category;
  final String? createdAt;
  final String? readAt;

  bool get isRead => readAt != null;
}

/// `GET /notifications/unread-count` response.
class UnreadCount {
  const UnreadCount({this.count = 0});

  factory UnreadCount.fromJson(Map<String, dynamic> json) =>
      UnreadCount(count: JsonX.intOrNull(json['count']) ?? 0);

  final int count;
}
