import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../core/network/chat_realtime.dart';
import '../models/chat_models.dart';
import 'api_provider.dart';

/// Shared SignalR connection for chat + calling.
final chatRealtimeProvider = Provider<ChatRealtime>((ref) {
  final rt = ChatRealtime(ref.watch(tokenStorageProvider));
  ref.onDispose(rt.dispose);
  return rt;
});

/// `GET /chat/conversations`.
final conversationsProvider = FutureProvider<List<ChatConversation>>((
  ref,
) async {
  final api = ref.watch(apiClientProvider);
  final list = await api.getList('/chat/conversations');
  return list
      .whereType<Map>()
      .map((e) => ChatConversation.fromJson(Map<String, dynamic>.from(e)))
      .toList(growable: false);
});

/// `GET /chat/conversations/{id}`.
final conversationDetailProvider =
    FutureProvider.family<ChatConversationDetail, String>((ref, id) async {
      final api = ref.watch(apiClientProvider);
      final json = await api.getMap('/chat/conversations/$id');
      return ChatConversationDetail.fromJson(json);
    });

/// `GET /chat/contacts?search=`.
final chatContactsProvider = FutureProvider.family<List<ChatContact>, String>((
  ref,
  search,
) async {
  final api = ref.watch(apiClientProvider);
  final list = await api.getList(
    '/chat/contacts',
    query: search.trim().isEmpty ? null : {'search': search.trim()},
  );
  return list
      .whereType<Map>()
      .map((e) => ChatContact.fromJson(Map<String, dynamic>.from(e)))
      .toList(growable: false);
});

/// `GET /chat/calls/history`.
final callHistoryProvider = FutureProvider<List<CallHistoryItem>>((ref) async {
  final api = ref.watch(apiClientProvider);
  final list = await api.getList('/chat/calls/history');
  return list
      .whereType<Map>()
      .map((e) => CallHistoryItem.fromJson(Map<String, dynamic>.from(e)))
      .toList(growable: false);
});

/// `GET /chat/unread-count`.
final chatUnreadProvider = FutureProvider<int>((ref) async {
  final api = ref.watch(apiClientProvider);
  final json = await api.getMap('/chat/unread-count');
  return (json['count'] as num?)?.toInt() ?? 0;
});

/// Chat + calling write actions.
class ChatActions {
  ChatActions(this._ref);
  final Ref _ref;

  Future<ChatConversation> startDirect(String employeeId) async {
    final api = _ref.read(apiClientProvider);
    final json = await api.postMap(
      '/chat/conversations/direct',
      data: {'employeeId': employeeId},
    );
    return ChatConversation.fromJson(json);
  }

  Future<ChatConversation> createGroup(
    String name,
    List<String> memberIds,
  ) async {
    final api = _ref.read(apiClientProvider);
    final json = await api.postMap(
      '/chat/conversations/group',
      data: {'name': name, 'memberIds': memberIds},
    );
    return ChatConversation.fromJson(json);
  }

  Future<ChatMessage> sendText(String conversationId, String body) async {
    final api = _ref.read(apiClientProvider);
    final json = await api.postMap(
      '/chat/conversations/$conversationId/messages',
      data: {'body': body},
    );
    return ChatMessage.fromJson(json);
  }

  Future<ChatMessage> sendAttachment(
    String conversationId, {
    required String filePath,
    required String kind,
    int? durationSeconds,
  }) async {
    final api = _ref.read(apiClientProvider);
    final form = FormData.fromMap({
      'file': await MultipartFile.fromFile(filePath),
      'kind': kind,
      if (durationSeconds != null) 'durationSeconds': '$durationSeconds',
    });
    final json = await api.postMultipart(
      '/chat/conversations/$conversationId/attachments',
      formData: form,
    );
    return ChatMessage.fromJson(json);
  }

  Future<void> markRead(String conversationId) async {
    await _ref
        .read(apiClientProvider)
        .postEmpty('/chat/conversations/$conversationId/read');
  }

  Future<void> updateGroupMembers(
    String conversationId,
    List<String> memberIds,
  ) async {
    await _ref
        .read(apiClientProvider)
        .putEmpty(
          '/chat/conversations/$conversationId/members',
          data: {'memberIds': memberIds},
        );
  }

  Future<void> leave(String conversationId) async {
    await _ref
        .read(apiClientProvider)
        .postEmpty('/chat/conversations/$conversationId/leave');
  }

  /// Register the device's FCM token for push notifications.
  Future<void> registerDevice(
    String token, {
    String platform = 'android',
  }) async {
    await _ref
        .read(apiClientProvider)
        .postEmpty(
          '/chat/devices',
          data: {'token': token, 'platform': platform},
        );
  }

  // ---- calling ----

  Future<String> startCall(String calleeEmployeeId, String kind) async {
    final api = _ref.read(apiClientProvider);
    final json = await api.postMap(
      '/chat/calls',
      data: {'calleeEmployeeId': calleeEmployeeId, 'kind': kind},
    );
    return json['callId']?.toString() ?? '';
  }

  Future<void> endCall(
    String callId,
    String state, {
    int? durationSeconds,
  }) async {
    await _ref
        .read(apiClientProvider)
        .postEmpty(
          '/chat/calls/$callId/end',
          data: {
            'state': state,
            if (durationSeconds != null) 'durationSeconds': durationSeconds,
          },
        );
  }
}

final chatActionsProvider = Provider<ChatActions>((ref) => ChatActions(ref));
