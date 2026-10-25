import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:signalr_netcore/signalr_client.dart';

import '../config/env.dart';
import '../storage/token_storage.dart';

/// Payloads surfaced to the UI from the chat hub.
class IncomingCall {
  IncomingCall({
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

class SignalPayload {
  SignalPayload(this.callId, this.fromEmployeeId, this.data);
  final String callId;
  final String fromEmployeeId;
  final Map<String, dynamic> data;
}

/// Thin wrapper over the SignalR connection to `/hubs/chat`. Exposes broadcast
/// streams for messages, conversation refreshes and WebRTC call signaling.
class ChatRealtime {
  ChatRealtime(this._tokenStorage);

  final TokenStorage _tokenStorage;
  HubConnection? _hub;
  bool _connecting = false;

  final _messages = StreamController<Map<String, dynamic>>.broadcast();
  final _conversationUpdated = StreamController<void>.broadcast();
  final _incomingCall = StreamController<IncomingCall>.broadcast();
  final _callEnded = StreamController<SignalPayload>.broadcast();
  final _callOffer = StreamController<SignalPayload>.broadcast();
  final _callAnswer = StreamController<SignalPayload>.broadcast();
  final _callIce = StreamController<SignalPayload>.broadcast();
  final _callState = StreamController<SignalPayload>.broadcast();
  final _connected = StreamController<bool>.broadcast();

  Stream<Map<String, dynamic>> get messages => _messages.stream;
  Stream<void> get conversationUpdated => _conversationUpdated.stream;
  Stream<IncomingCall> get incomingCall => _incomingCall.stream;
  Stream<SignalPayload> get callEnded => _callEnded.stream;
  Stream<SignalPayload> get callOffer => _callOffer.stream;
  Stream<SignalPayload> get callAnswer => _callAnswer.stream;
  Stream<SignalPayload> get callIce => _callIce.stream;
  Stream<SignalPayload> get callState => _callState.stream;

  /// Emits true/false as the hub connects/disconnects (for a UI status chip).
  Stream<bool> get connectionState => _connected.stream;

  bool get isConnected => _hub?.state == HubConnectionState.Connected;

  Future<void> connect() async {
    if (_hub != null && isConnected) return;
    if (_connecting) return;
    _connecting = true;
    try {
      // Build the absolute hub URL. Env.apiBaseUrl is the host without /api/v1.
      final url = '${Env.apiBaseUrl}/hubs/chat';
      final options = HttpConnectionOptions(
        accessTokenFactory: () async =>
            await _tokenStorage.readAccessToken() ?? '',
      );
      final hub = HubConnectionBuilder()
          .withUrl(url, options: options)
          .withAutomaticReconnect(retryDelays: [0, 2000, 5000, 10000, 15000])
          .build();
      hub.serverTimeoutInMilliseconds = 60000;
      hub.keepAliveIntervalInMilliseconds = 15000;

      hub.on('message', (args) => _emitMap(_messages, args));
      hub.on('conversationUpdated', (_) => _conversationUpdated.add(null));
      hub.on('incomingCall', (args) {
        final m = _first(args);
        if (m != null) {
          _incomingCall.add(
            IncomingCall(
              callId: '${m['callId']}',
              fromEmployeeId: '${m['fromEmployeeId']}',
              fromName: '${m['fromName']}',
              kind: '${m['kind']}',
            ),
          );
        }
      });
      hub.on('callEnded', (args) => _emitSignal(_callEnded, args));
      hub.on('callOffer', (args) => _emitSignal(_callOffer, args));
      hub.on('callAnswer', (args) => _emitSignal(_callAnswer, args));
      hub.on('callIce', (args) => _emitSignal(_callIce, args));
      hub.on('callState', (args) => _emitSignal(_callState, args));

      hub.onclose(({error}) {
        debugPrint('SignalR closed: $error');
        _connected.add(false);
      });
      hub.onreconnected(({connectionId}) => _connected.add(true));
      hub.onreconnecting(({error}) => _connected.add(false));

      await hub.start();
      _hub = hub;
      _connected.add(true);
      debugPrint('SignalR connected to $url');
    } catch (e) {
      debugPrint('ChatRealtime connect failed: $e');
      _connected.add(false);
    } finally {
      _connecting = false;
    }
  }

  // ---- outgoing WebRTC signaling ----

  Future<void> sendOffer(
    String targetEmployeeId,
    String callId,
    String kind,
    Map<String, dynamic> sdp,
  ) => _invoke('SendOffer', [targetEmployeeId, callId, kind, sdp]);

  Future<void> sendAnswer(
    String targetEmployeeId,
    String callId,
    Map<String, dynamic> sdp,
  ) => _invoke('SendAnswer', [targetEmployeeId, callId, sdp]);

  Future<void> sendIce(
    String targetEmployeeId,
    String callId,
    Map<String, dynamic> candidate,
  ) => _invoke('SendIceCandidate', [targetEmployeeId, callId, candidate]);

  Future<void> signalCallState(
    String targetEmployeeId,
    String callId,
    String state,
  ) => _invoke('SignalCallState', [targetEmployeeId, callId, state]);

  Future<void> _invoke(String method, List<Object> args) async {
    if (!isConnected) await connect();
    try {
      await _hub?.invoke(method, args: args);
    } catch (e) {
      debugPrint('ChatRealtime invoke $method failed: $e');
    }
  }

  void _emitMap(
    StreamController<Map<String, dynamic>> ctrl,
    List<Object?>? args,
  ) {
    final m = _first(args);
    if (m != null) ctrl.add(m);
  }

  void _emitSignal(StreamController<SignalPayload> ctrl, List<Object?>? args) {
    final m = _first(args);
    if (m != null) {
      ctrl.add(
        SignalPayload(
          '${m['callId']}',
          '${m['fromEmployeeId']}',
          Map<String, dynamic>.from(m),
        ),
      );
    }
  }

  Map<String, dynamic>? _first(List<Object?>? args) {
    if (args == null || args.isEmpty) return null;
    final first = args.first;
    if (first is Map) return Map<String, dynamic>.from(first);
    return null;
  }

  Future<void> dispose() async {
    await _hub?.stop();
    await _messages.close();
    await _conversationUpdated.close();
    await _incomingCall.close();
    await _callEnded.close();
    await _callOffer.close();
    await _callAnswer.close();
    await _callIce.close();
    await _callState.close();
    await _connected.close();
  }
}
