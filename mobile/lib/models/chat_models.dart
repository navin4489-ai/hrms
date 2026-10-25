import 'common.dart';

/// `GET /chat/contacts` item.
class ChatContact {
  const ChatContact({
    required this.employeeId,
    required this.displayName,
    required this.employeeNumber,
    this.department,
    this.designation,
  });

  factory ChatContact.fromJson(Map<String, dynamic> json) => ChatContact(
    employeeId: JsonX.strOr(json['employeeId']),
    displayName: JsonX.strOr(json['displayName']),
    employeeNumber: JsonX.strOr(json['employeeNumber']),
    department: JsonX.str(json['department']),
    designation: JsonX.str(json['designation']),
  );

  final String employeeId;
  final String displayName;
  final String employeeNumber;
  final String? department;
  final String? designation;
}

/// `GET /chat/conversations` item.
class ChatConversation {
  const ChatConversation({
    required this.id,
    required this.type,
    required this.title,
    this.lastMessagePreview,
    required this.lastMessageAt,
    this.unreadCount = 0,
    this.memberCount = 0,
    this.otherEmployeeId,
  });

  factory ChatConversation.fromJson(Map<String, dynamic> json) =>
      ChatConversation(
        id: JsonX.strOr(json['id']),
        type: JsonX.strOr(json['type'], 'Direct'),
        title: JsonX.strOr(json['title']),
        lastMessagePreview: JsonX.str(json['lastMessagePreview']),
        lastMessageAt: JsonX.str(json['lastMessageAt']) ?? '',
        unreadCount: JsonX.intOrNull(json['unreadCount']) ?? 0,
        memberCount: JsonX.intOrNull(json['memberCount']) ?? 0,
        otherEmployeeId: JsonX.str(json['otherEmployeeId']),
      );

  final String id;
  final String type;
  final String title;
  final String? lastMessagePreview;
  final String lastMessageAt;
  final int unreadCount;
  final int memberCount;
  final String? otherEmployeeId;

  bool get isGroup => type == 'Group';
}

/// One chat message.
class ChatMessage {
  const ChatMessage({
    required this.id,
    required this.conversationId,
    required this.senderEmployeeId,
    required this.senderName,
    this.kind = 'Text',
    this.body,
    this.attachmentUrl,
    this.attachmentFileName,
    this.attachmentContentType,
    this.attachmentSize,
    this.durationSeconds,
    required this.createdAt,
    this.mine = false,
  });

  factory ChatMessage.fromJson(Map<String, dynamic> json) => ChatMessage(
    id: JsonX.strOr(json['id']),
    conversationId: JsonX.strOr(json['conversationId']),
    senderEmployeeId: JsonX.strOr(json['senderEmployeeId']),
    senderName: JsonX.strOr(json['senderName']),
    kind: JsonX.strOr(json['kind'], 'Text'),
    body: JsonX.str(json['body']),
    attachmentUrl: JsonX.str(json['attachmentUrl']),
    attachmentFileName: JsonX.str(json['attachmentFileName']),
    attachmentContentType: JsonX.str(json['attachmentContentType']),
    attachmentSize: JsonX.intOrNull(json['attachmentSize']),
    durationSeconds: JsonX.intOrNull(json['durationSeconds']),
    createdAt: JsonX.str(json['createdAt']) ?? '',
    mine: JsonX.boolOr(json['mine']),
  );

  final String id;
  final String conversationId;
  final String senderEmployeeId;
  final String senderName;
  final String kind;
  final String? body;
  final String? attachmentUrl;
  final String? attachmentFileName;
  final String? attachmentContentType;
  final int? attachmentSize;
  final int? durationSeconds;
  final String createdAt;
  final bool mine;

  bool get isImage => kind == 'Image';
  bool get isVoice => kind == 'VoiceNote';
  bool get isFile => kind == 'File';
}

class ChatConversationDetail {
  const ChatConversationDetail({
    required this.id,
    required this.type,
    required this.title,
    this.members = const [],
    this.messages = const [],
  });

  factory ChatConversationDetail.fromJson(Map<String, dynamic> json) =>
      ChatConversationDetail(
        id: JsonX.strOr(json['id']),
        type: JsonX.strOr(json['type'], 'Direct'),
        title: JsonX.strOr(json['title']),
        members: JsonX.mapList(json['members'])
            .map(ChatMember.fromJson)
            .toList(growable: false),
        messages: JsonX.mapList(json['messages'])
            .map(ChatMessage.fromJson)
            .toList(growable: false),
      );

  final String id;
  final String type;
  final String title;
  final List<ChatMember> members;
  final List<ChatMessage> messages;

  bool get isGroup => type == 'Group';
}

class ChatMember {
  const ChatMember({
    required this.employeeId,
    required this.displayName,
    this.isAdmin = false,
  });

  factory ChatMember.fromJson(Map<String, dynamic> json) => ChatMember(
    employeeId: JsonX.strOr(json['employeeId']),
    displayName: JsonX.strOr(json['displayName']),
    isAdmin: JsonX.boolOr(json['isAdmin']),
  );

  final String employeeId;
  final String displayName;
  final bool isAdmin;
}

/// `GET /chat/calls/history` item.
class CallHistoryItem {
  const CallHistoryItem({
    required this.id,
    required this.otherEmployeeId,
    required this.otherName,
    this.kind = 'Audio',
    this.state = 'Ended',
    this.outgoing = false,
    required this.startedAt,
    this.durationSeconds,
  });

  factory CallHistoryItem.fromJson(Map<String, dynamic> json) =>
      CallHistoryItem(
        id: JsonX.strOr(json['id']),
        otherEmployeeId: JsonX.strOr(json['otherEmployeeId']),
        otherName: JsonX.strOr(json['otherName']),
        kind: JsonX.strOr(json['kind'], 'Audio'),
        state: JsonX.strOr(json['state'], 'Ended'),
        outgoing: JsonX.boolOr(json['outgoing']),
        startedAt: JsonX.str(json['startedAt']) ?? '',
        durationSeconds: JsonX.intOrNull(json['durationSeconds']),
      );

  final String id;
  final String otherEmployeeId;
  final String otherName;
  final String kind;
  final String state;
  final bool outgoing;
  final String startedAt;
  final int? durationSeconds;

  bool get isVideo => kind == 'Video';
  bool get isMissed => state == 'Missed' || state == 'Declined';
}
