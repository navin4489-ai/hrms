import 'dart:convert';
import 'dart:typed_data';

import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:wintech_hrms/core/network/api_client.dart';
import 'package:wintech_hrms/core/network/api_exception.dart';
import 'package:wintech_hrms/core/network/chat_realtime.dart';
import 'package:wintech_hrms/core/storage/token_storage.dart';
import 'package:wintech_hrms/features/chat/conversation_screen.dart';
import 'package:wintech_hrms/models/chat_models.dart';
import 'package:wintech_hrms/providers/api_provider.dart';
import 'package:wintech_hrms/providers/chat_provider.dart';

class _Api extends ApiClient {
  _Api() : super(tokenStorage: InMemoryTokenStorage());
  bool missing = false;
  String? downloadedPath;

  @override
  Future<int> postEmpty(String path, {Object? data}) async => 204;

  @override
  Future<List<int>> downloadBytes(
    String path, {
    Map<String, dynamic>? query,
  }) async {
    downloadedPath = path;
    if (missing) {
      throw const ApiException(
        statusCode: 404,
        detail: 'The requested file is no longer available.',
      );
    }
    return base64Decode(
      'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j8ioAAAAASUVORK5CYII=',
    );
  }
}

class _Realtime extends ChatRealtime {
  _Realtime() : super(InMemoryTokenStorage());
  @override
  Future<void> connect() async {}
}

class _SavePicker extends FilePickerPlatform {
  String? savedName;
  Uint8List? savedBytes;

  @override
  Future<Uri?> saveFile({
    required String fileName,
    required Uint8List bytes,
    required String mimeType,
    String? dialogTitle,
    String? initialDirectory,
    Function(FilePickerStatus)? onFileSaving,
    WindowsOptions windowsOptions = const WindowsOptions(),
    LinuxOptions linuxOptions = const LinuxOptions(),
    WebOptions webOptions = const WebOptions(),
  }) async {
    savedName = fileName;
    savedBytes = bytes;
    return Uri.parse('content://downloads/$fileName');
  }
}

void main() {
  Future<void> openChat(WidgetTester tester, _Api api) async {
    final rt = _Realtime();
    addTearDown(api.close);
    addTearDown(rt.dispose);
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          apiClientProvider.overrideWithValue(api),
          chatRealtimeProvider.overrideWithValue(rt),
          conversationDetailProvider.overrideWith(
            (ref, id) async => const ChatConversationDetail(
              id: 'conversation',
              type: 'Direct',
              title: 'Test chat',
              messages: [
                ChatMessage(
                  id: 'image-message',
                  conversationId: 'conversation',
                  senderEmployeeId: 'sender',
                  senderName: 'Sender',
                  kind: 'Image',
                  attachmentFileName: 'photo.png',
                  createdAt: '2026-10-26T09:00:00Z',
                ),
              ],
            ),
          ),
        ],
        child: const MaterialApp(
          home: ConversationScreen(
            conversationId: 'conversation',
            title: 'Test chat',
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets(
    'image tap downloads through authenticated API and opens zoomable viewer with Save',
    (tester) async {
      final api = _Api();
      await openChat(tester, api);
      await tester.tap(find.text('Photo · tap to view'));
      await tester.pumpAndSettle();
      expect(api.downloadedPath, '/chat/messages/image-message/attachment');
      expect(find.byType(InteractiveViewer), findsOneWidget);
      expect(find.byIcon(Icons.download), findsOneWidget);
      await tester.tap(find.byIcon(Icons.close));
      await tester.pumpAndSettle();
      expect(find.byType(Dialog), findsNothing);
    },
  );

  testWidgets(
    'missing server file shows resend explanation instead of a broken viewer',
    (tester) async {
      final api = _Api()..missing = true;
      await openChat(tester, api);
      await tester.tap(find.text('Photo · tap to view'));
      await tester.pumpAndSettle();
      expect(find.textContaining('ask the sender to resend'), findsOneWidget);
      expect(find.byType(Dialog), findsNothing);
    },
  );

  testWidgets('image Save sends actual bytes and filename to the system destination picker', (tester) async {
    final previous = FilePickerPlatform.instance;
    final picker = _SavePicker();
    FilePickerPlatform.instance = picker;
    addTearDown(() => FilePickerPlatform.instance = previous);
    await openChat(tester, _Api());
    await tester.tap(find.text('Photo · tap to view'));
    await tester.pumpAndSettle();
    await tester.tap(find.byIcon(Icons.download));
    await tester.pumpAndSettle();
    expect(picker.savedName, 'photo.png');
    expect(picker.savedBytes, isNotEmpty);
    expect(picker.savedBytes!.take(4), [137, 80, 78, 71]);
  });
}
