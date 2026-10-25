import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_riverpod/legacy.dart';
import 'package:flutter_webrtc/flutter_webrtc.dart';
import 'package:permission_handler/permission_handler.dart';
import 'package:wakelock_plus/wakelock_plus.dart';

import '../core/network/chat_realtime.dart';
import '../core/network/push_notifications.dart';
import 'chat_provider.dart';

enum CallPhase { idle, ringing, connecting, connected, ended }

/// Holds an active WebRTC call independently of any screen, so the call survives
/// navigation (back / minimize). The full-screen call UI and the floating mini
/// window both render from this single controller. Keeps the screen awake while
/// a call is ongoing.
class CallController extends ChangeNotifier {
  CallController(this._ref);

  final Ref _ref;

  final localRenderer = RTCVideoRenderer();
  final remoteRenderer = RTCVideoRenderer();

  final List<StreamSubscription<dynamic>> _subs = [];
  final List<RTCIceCandidate> _pendingCandidates = [];

  RTCPeerConnection? _pc;
  MediaStream? _localStream;

  CallPhase phase = CallPhase.idle;
  String? callId;
  String peerEmployeeId = '';
  String peerName = '';
  String kind = 'Audio'; // 'Audio' | 'Video'
  bool outgoing = false;
  bool muted = false;
  bool videoOff = false;
  bool speaker = true;
  bool minimized = false;
  String statusText = '';
  DateTime? connectedAt;

  bool _remoteSet = false;
  bool _offerSent = false;
  bool _renderersReady = false;

  bool get isVideo => kind == 'Video';
  bool get isActive => phase != CallPhase.idle && phase != CallPhase.ended;

  static const Map<String, dynamic> _config = {
    'iceServers': [
      {'urls': 'stun:stun.l.google.com:19302'},
      {'urls': 'stun:stun1.l.google.com:19302'},
    ],
    'sdpSemantics': 'unified-plan',
  };

  Future<void> _ensureRenderers() async {
    if (_renderersReady) return;
    await localRenderer.initialize();
    await remoteRenderer.initialize();
    _renderersReady = true;
  }

  /// Start (or accept) a call. Returns immediately; UI listens to this controller.
  Future<void> start({
    required String peerEmployeeId,
    required String peerName,
    required String kind,
    required bool outgoing,
    String? callId,
  }) async {
    if (isActive) return; // one call at a time
    this.peerEmployeeId = peerEmployeeId;
    this.peerName = peerName;
    this.kind = kind;
    this.outgoing = outgoing;
    this.callId = callId;
    minimized = false;
    muted = false;
    videoOff = false;
    speaker = true;
    connectedAt = null;
    _remoteSet = false;
    _offerSent = false;
    _pendingCandidates.clear();
    phase = CallPhase.ringing;
    statusText = outgoing ? 'Ringing…' : 'Incoming…';
    notifyListeners();

    await _ensureRenderers();
    WakelockPlus.enable();
    PushNotifications.instance.startRingtone();

    await (isVideo
            ? [Permission.microphone, Permission.camera]
            : [Permission.microphone])
        .request();

    final rt = _ref.read(chatRealtimeProvider);
    await rt.connect();
    _bindSignaling(rt);

    try {
      _localStream = await navigator.mediaDevices.getUserMedia({
        'audio': true,
        'video': isVideo
            ? {'facingMode': 'user', 'width': 640, 'height': 480}
            : false,
      });
      localRenderer.srcObject = _localStream;
    } catch (_) {
      statusText = 'Microphone/camera permission is required.';
      notifyListeners();
      await end(notify: false);
      return;
    }

    _pc = await createPeerConnection(_config);
    for (final track in _localStream!.getTracks()) {
      await _pc!.addTrack(track, _localStream!);
    }

    _pc!.onIceCandidate = (candidate) {
      if (this.callId != null && candidate.candidate != null) {
        rt.sendIce(this.peerEmployeeId, this.callId!, {
          'candidate': candidate.candidate,
          'sdpMid': candidate.sdpMid,
          'sdpMLineIndex': candidate.sdpMLineIndex,
        });
      }
    };
    _pc!.onTrack = (event) {
      if (event.streams.isNotEmpty) {
        remoteRenderer.srcObject = event.streams.first;
        _markConnected();
      }
    };
    _pc!.onConnectionState = (state) {
      if (state == RTCPeerConnectionState.RTCPeerConnectionStateConnected) {
        _markConnected();
      } else if (state ==
              RTCPeerConnectionState.RTCPeerConnectionStateDisconnected ||
          state == RTCPeerConnectionState.RTCPeerConnectionStateFailed ||
          state == RTCPeerConnectionState.RTCPeerConnectionStateClosed) {
        end(notify: false);
      }
    };

    if (outgoing) {
      this.callId ??= await _ref
          .read(chatActionsProvider)
          .startCall(peerEmployeeId, kind);
    } else {
      if (this.callId != null) {
        await rt.signalCallState(peerEmployeeId, this.callId!, 'ready');
      }
    }
  }

  void _bindSignaling(ChatRealtime rt) {
    _subs.add(
      rt.callState.listen((p) async {
        if (!outgoing || p.callId != callId) return;
        if ('${p.data['state']}' == 'ready' && !_offerSent) {
          _offerSent = true;
          await _sendOffer(rt);
        }
      }),
    );
    _subs.add(
      rt.callOffer.listen((p) async {
        if (callId != null && p.callId != callId) return;
        callId ??= p.callId;
        final sdp = p.data['sdp'] as Map?;
        if (sdp == null || _pc == null) return;
        phase = CallPhase.connecting;
        notifyListeners();
        await _pc!.setRemoteDescription(
          RTCSessionDescription('${sdp['sdp']}', '${sdp['type']}'),
        );
        _remoteSet = true;
        await _drain();
        final answer = await _pc!.createAnswer();
        await _pc!.setLocalDescription(answer);
        await rt.sendAnswer(peerEmployeeId, callId!, {
          'sdp': answer.sdp,
          'type': answer.type,
        });
      }),
    );
    _subs.add(
      rt.callAnswer.listen((p) async {
        if (p.callId != callId) return;
        final sdp = p.data['sdp'] as Map?;
        if (sdp == null || _pc == null) return;
        await _pc!.setRemoteDescription(
          RTCSessionDescription('${sdp['sdp']}', '${sdp['type']}'),
        );
        _remoteSet = true;
        await _drain();
      }),
    );
    _subs.add(
      rt.callIce.listen((p) async {
        if (p.callId != callId) return;
        final c = p.data['candidate'] as Map?;
        if (c == null || _pc == null) return;
        final cand = RTCIceCandidate(
          '${c['candidate']}',
          c['sdpMid'] as String?,
          (c['sdpMLineIndex'] as num?)?.toInt(),
        );
        if (_remoteSet) {
          await _pc!.addCandidate(cand);
        } else {
          _pendingCandidates.add(cand);
        }
      }),
    );
    _subs.add(
      rt.callEnded.listen((p) {
        if (p.callId == callId) end(notify: false);
      }),
    );
  }

  Future<void> _sendOffer(ChatRealtime rt) async {
    if (_pc == null || callId == null) return;
    phase = CallPhase.connecting;
    notifyListeners();
    final offer = await _pc!.createOffer({
      'offerToReceiveAudio': true,
      'offerToReceiveVideo': isVideo,
    });
    await _pc!.setLocalDescription(offer);
    await rt.sendOffer(peerEmployeeId, callId!, kind, {
      'sdp': offer.sdp,
      'type': offer.type,
    });
  }

  Future<void> _drain() async {
    for (final c in _pendingCandidates) {
      try {
        await _pc!.addCandidate(c);
      } catch (_) {}
    }
    _pendingCandidates.clear();
  }

  void _markConnected() {
    if (phase == CallPhase.connected || phase == CallPhase.ended) return;
    PushNotifications.instance.stopRingtone();
    Helper.setSpeakerphoneOn(speaker);
    phase = CallPhase.connected;
    connectedAt ??= DateTime.now();
    statusText = 'Connected';
    notifyListeners();
  }

  void toggleMute() {
    muted = !muted;
    for (final t in _localStream?.getAudioTracks() ?? []) {
      t.enabled = !muted;
    }
    notifyListeners();
  }

  void toggleVideo() {
    videoOff = !videoOff;
    for (final t in _localStream?.getVideoTracks() ?? []) {
      t.enabled = !videoOff;
    }
    notifyListeners();
  }

  void toggleSpeaker() {
    speaker = !speaker;
    Helper.setSpeakerphoneOn(speaker);
    notifyListeners();
  }

  void setMinimized(bool value) {
    minimized = value;
    notifyListeners();
  }

  /// End the call and tear down all resources.
  Future<void> end({bool notify = true}) async {
    if (phase == CallPhase.ended || phase == CallPhase.idle) {
      phase = CallPhase.ended;
      notifyListeners();
      return;
    }
    final seconds = connectedAt == null
        ? null
        : DateTime.now().difference(connectedAt!).inSeconds;
    final id = callId;
    phase = CallPhase.ended;
    notifyListeners();

    if (notify && id != null) {
      try {
        await _ref
            .read(chatActionsProvider)
            .endCall(id, 'Ended', durationSeconds: seconds);
      } catch (_) {}
    }

    await PushNotifications.instance.stopRingtone();
    WakelockPlus.disable();
    for (final s in _subs) {
      await s.cancel();
    }
    _subs.clear();
    try {
      for (final t in _localStream?.getTracks() ?? []) {
        await t.stop();
      }
    } catch (_) {}
    await _localStream?.dispose();
    _localStream = null;
    await _pc?.close();
    _pc = null;
    localRenderer.srcObject = null;
    remoteRenderer.srcObject = null;

    _ref.invalidate(callHistoryProvider);

    // Return to idle so the UI removes the overlay.
    phase = CallPhase.idle;
    callId = null;
    minimized = false;
    connectedAt = null;
    notifyListeners();
  }

  @override
  void dispose() {
    localRenderer.dispose();
    remoteRenderer.dispose();
    super.dispose();
  }
}

final callControllerProvider = ChangeNotifierProvider<CallController>(
  (ref) => CallController(ref),
);
