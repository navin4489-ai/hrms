import 'dart:async';

import 'package:audioplayers/audioplayers.dart';
import 'package:firebase_core/firebase_core.dart';
import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_local_notifications/flutter_local_notifications.dart';

/// A parsed incoming-call payload delivered via push (used to raise the ringing UI).
class PushCall {
  PushCall({
    required this.callId,
    required this.fromEmployeeId,
    required this.fromName,
    required this.kind,
  });
  final String callId;
  final String fromEmployeeId;
  final String fromName;
  final String kind;
}

/// Handles FCM push notifications and call ringing.
///
/// Degrades gracefully: if Firebase is not configured (no google-services.json / init fails),
/// every method becomes a safe no-op and the app still works via in-app SignalR realtime.
class PushNotifications {
  PushNotifications._();
  static final PushNotifications instance = PushNotifications._();

  final _local = FlutterLocalNotificationsPlugin();
  final _ringPlayer = AudioPlayer();
  bool _available = false;
  bool _initialized = false;

  final _incomingCall = StreamController<PushCall>.broadcast();
  final _openConversation = StreamController<String>.broadcast();

  /// Call payloads from push (when the app was backgrounded/terminated).
  Stream<PushCall> get incomingCall => _incomingCall.stream;

  /// Conversation id to open when a message notification is tapped.
  Stream<String> get openConversation => _openConversation.stream;

  bool get isAvailable => _available;

  /// Token to register with the API, or null when push is unavailable.
  Future<String?> getToken() async {
    if (!_available) return null;
    try {
      return await FirebaseMessaging.instance.getToken();
    } catch (_) {
      return null;
    }
  }

  Stream<String> get onTokenRefresh => _available
      ? FirebaseMessaging.instance.onTokenRefresh
      : const Stream.empty();

  Future<void> init() async {
    if (_initialized) return;
    _initialized = true;
    try {
      await Firebase.initializeApp();
      _available = true;
    } catch (e) {
      // No Firebase config on this build — push disabled, in-app realtime still works.
      debugPrint('Push disabled (Firebase not configured): $e');
      _available = false;
      return;
    }

    await _initLocalNotifications();

    final messaging = FirebaseMessaging.instance;
    await messaging.requestPermission(alert: true, badge: true, sound: true);
    await messaging.setForegroundNotificationPresentationOptions(
      alert: true,
      badge: true,
      sound: true,
    );

    FirebaseMessaging.onMessage.listen(_handleMessage);
    FirebaseMessaging.onMessageOpenedApp.listen(_handleOpenedFromNotification);

    // App launched from a terminated state by tapping a notification.
    final initial = await messaging.getInitialMessage();
    if (initial != null) _handleOpenedFromNotification(initial);
  }

  Future<void> _initLocalNotifications() async {
    const androidInit = AndroidInitializationSettings('@mipmap/ic_launcher');
    const settings = InitializationSettings(android: androidInit);
    await _local.initialize(
      settings,
      onDidReceiveNotificationResponse: (response) {
        final payload = response.payload;
        if (payload != null && payload.startsWith('conv:')) {
          _openConversation.add(payload.substring(5));
        }
      },
    );
    // High-importance channels for messages and calls.
    final androidPlugin = _local
        .resolvePlatformSpecificImplementation<
          AndroidFlutterLocalNotificationsPlugin
        >();
    await androidPlugin?.createNotificationChannel(
      const AndroidNotificationChannel(
        'chat_messages',
        'Chat messages',
        description: 'New chat messages',
        importance: Importance.high,
      ),
    );
    await androidPlugin?.createNotificationChannel(
      const AndroidNotificationChannel(
        'calls',
        'Incoming calls',
        description: 'Incoming voice and video calls',
        importance: Importance.max,
      ),
    );
    await androidPlugin?.requestNotificationsPermission();
  }

  void _handleMessage(RemoteMessage message) {
    final data = message.data;
    final type = data['type'];
    if (type == 'call') {
      _incomingCall.add(
        PushCall(
          callId: data['callId'] ?? '',
          fromEmployeeId: data['fromEmployeeId'] ?? '',
          fromName: data['fromName'] ?? 'Caller',
          kind: data['kind'] ?? 'Audio',
        ),
      );
    } else if (type == 'message') {
      _showMessageNotification(
        title: data['title'] ?? message.notification?.title ?? 'New message',
        body: data['body'] ?? message.notification?.body ?? '',
        conversationId: data['conversationId'] ?? '',
      );
    }
  }

  void _handleOpenedFromNotification(RemoteMessage message) {
    final data = message.data;
    if (data['type'] == 'message' &&
        (data['conversationId'] ?? '').isNotEmpty) {
      _openConversation.add(data['conversationId']);
    } else if (data['type'] == 'call') {
      _incomingCall.add(
        PushCall(
          callId: data['callId'] ?? '',
          fromEmployeeId: data['fromEmployeeId'] ?? '',
          fromName: data['fromName'] ?? 'Caller',
          kind: data['kind'] ?? 'Audio',
        ),
      );
    }
  }

  Future<void> _showMessageNotification({
    required String title,
    required String body,
    required String conversationId,
  }) async {
    await _local.show(
      conversationId.hashCode,
      title,
      body,
      const NotificationDetails(
        android: AndroidNotificationDetails(
          'chat_messages',
          'Chat messages',
          importance: Importance.high,
          priority: Priority.high,
        ),
      ),
      payload: 'conv:$conversationId',
    );
  }

  /// Plays the ringtone in a loop (for the ringing call UI).
  Future<void> startRingtone() async {
    try {
      await _ringPlayer.setReleaseMode(ReleaseMode.loop);
      await _ringPlayer.play(AssetSource('sounds/ringtone.wav'), volume: 1.0);
    } catch (e) {
      // Ringtone asset may be absent; vibration (below) still alerts the user.
      debugPrint('Ringtone play failed: $e');
    }
  }

  Future<void> stopRingtone() async {
    try {
      await _ringPlayer.stop();
    } catch (_) {}
  }

  void dispose() {
    _ringPlayer.dispose();
    _incomingCall.close();
    _openConversation.close();
  }
}

/// Background message handler — must be a top-level function.
@pragma('vm:entry-point')
Future<void> firebaseBackgroundHandler(RemoteMessage message) async {
  // Background message receipt is enough for the OS to display a notification.
  // Data-only call pushes are handled when the app is opened.
}
