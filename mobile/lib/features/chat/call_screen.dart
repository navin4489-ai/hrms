import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_webrtc/flutter_webrtc.dart';
import 'package:permission_handler/permission_handler.dart';

import '../../core/network/chat_realtime.dart';
import '../../core/network/push_notifications.dart';
import '../../providers/chat_provider.dart';

/// 1:1 WebRTC audio/video call screen. Media is peer-to-peer; signaling flows
/// over the SignalR chat hub. Supports an outgoing caller and an incoming callee.
///
/// Handshake (fixes the "offer lost before callee was listening" race):
///  1. caller.startCall -> callee gets incomingCall (rings)
///  2. callee opens this screen, prepares media, sends "ready"
///  3. caller (on "ready") creates & sends the SDP offer
///  4. callee answers; ICE candidates are buffered until the remote SDP is set
class CallScreen extends ConsumerStatefulWidget {
  const CallScreen({
    super.key,
    required this.employeeId,
    required this.name,
    required this.kind,
    required this.outgoing,
    this.callId,
  });

  final String employeeId;
  final String name;
  final String kind; // 'Audio' | 'Video'
  final bool outgoing;
  final String? callId;

  @override
  ConsumerState<CallScreen> createState() => _CallScreenState();
}

class _CallScreenState extends ConsumerState<CallScreen> {
  final _localRenderer = RTCVideoRenderer();
  final _remoteRenderer = RTCVideoRenderer();
  final List<StreamSubscription<dynamic>> _subs = [];
  final List<RTCIceCandidate> _pendingCandidates = [];

  RTCPeerConnection? _pc;
  MediaStream? _localStream;
  String? _callId;
  String _status = 'Connecting…';
  bool _muted = false;
  bool _videoOff = false;
  bool _speaker = true;
  bool _remoteDescriptionSet = false;
  bool _offerSent = false;
  bool _ended = false;
  DateTime? _connectedAt;

  bool get _isVideo => widget.kind == 'Video';

  static const Map<String, dynamic> _config = {
    'iceServers': [
      {'urls': 'stun:stun.l.google.com:19302'},
      {'urls': 'stun:stun1.l.google.com:19302'},
    ],
    'sdpSemantics': 'unified-plan',
  };

  @override
  void initState() {
    super.initState();
    _callId = widget.callId;
    _start();
  }

  @override
  void dispose() {
    _cleanup();
    super.dispose();
  }

  Future<void> _start() async {
    await _localRenderer.initialize();
    await _remoteRenderer.initialize();

    // Both sides ring/ringback until media connects.
    PushNotifications.instance.startRingtone();
    setState(() => _status = widget.outgoing ? 'Ringing…' : 'Incoming…');

    // Permissions up front.
    final perms = _isVideo
        ? [Permission.microphone, Permission.camera]
        : [Permission.microphone];
    await perms.request();

    final rt = ref.read(chatRealtimeProvider);
    await rt.connect();
    _bindSignaling(rt);

    // Local media.
    try {
      _localStream = await navigator.mediaDevices.getUserMedia({
        'audio': true,
        'video': _isVideo
            ? {'facingMode': 'user', 'width': 640, 'height': 480}
            : false,
      });
      _localRenderer.srcObject = _localStream;
    } catch (e) {
      _fail('Microphone/camera permission is required.');
      return;
    }

    // Peer connection.
    _pc = await createPeerConnection(_config);
    for (final track in _localStream!.getTracks()) {
      await _pc!.addTrack(track, _localStream!);
    }

    _pc!.onIceCandidate = (candidate) {
      if (_callId != null && candidate.candidate != null) {
        rt.sendIce(widget.employeeId, _callId!, {
          'candidate': candidate.candidate,
          'sdpMid': candidate.sdpMid,
          'sdpMLineIndex': candidate.sdpMLineIndex,
        });
      }
    };

    _pc!.onTrack = (event) {
      if (event.streams.isNotEmpty) {
        _remoteRenderer.srcObject = event.streams.first;
        _markConnected();
      }
    };

    _pc!.onConnectionState = (state) {
      switch (state) {
        case RTCPeerConnectionState.RTCPeerConnectionStateConnected:
          _markConnected();
          break;
        case RTCPeerConnectionState.RTCPeerConnectionStateDisconnected:
        case RTCPeerConnectionState.RTCPeerConnectionStateFailed:
        case RTCPeerConnectionState.RTCPeerConnectionStateClosed:
          _hangUp(notify: false, state: 'Ended');
          break;
        default:
          break;
      }
    };

    if (widget.outgoing) {
      // Caller: create the call row now; wait for callee "ready" before the offer.
      _callId ??= await ref
          .read(chatActionsProvider)
          .startCall(widget.employeeId, widget.kind);
    } else {
      // Callee: tell the caller we're ready to receive the offer.
      _callId ??= widget.callId;
      if (_callId != null) {
        await rt.signalCallState(widget.employeeId, _callId!, 'ready');
      }
    }
  }

  void _bindSignaling(ChatRealtime rt) {
    // Callee signalled "ready" -> caller sends the offer.
    _subs.add(
      rt.callState.listen((p) async {
        if (!widget.outgoing) return;
        if (p.callId != _callId) return;
        final state = '${p.data['state']}';
        if (state == 'ready' && !_offerSent) {
          _offerSent = true;
          await _sendOffer(rt);
        }
      }),
    );

    // Callee receives the offer -> answers.
    _subs.add(
      rt.callOffer.listen((p) async {
        if (_callId != null && p.callId != _callId) return;
        _callId ??= p.callId;
        final sdp = p.data['sdp'] as Map?;
        if (sdp == null || _pc == null) return;
        await _pc!.setRemoteDescription(
          RTCSessionDescription('${sdp['sdp']}', '${sdp['type']}'),
        );
        _remoteDescriptionSet = true;
        await _drainCandidates();
        final answer = await _pc!.createAnswer();
        await _pc!.setLocalDescription(answer);
        await rt.sendAnswer(widget.employeeId, _callId!, {
          'sdp': answer.sdp,
          'type': answer.type,
        });
      }),
    );

    // Caller receives the answer.
    _subs.add(
      rt.callAnswer.listen((p) async {
        if (p.callId != _callId) return;
        final sdp = p.data['sdp'] as Map?;
        if (sdp == null || _pc == null) return;
        await _pc!.setRemoteDescription(
          RTCSessionDescription('${sdp['sdp']}', '${sdp['type']}'),
        );
        _remoteDescriptionSet = true;
        await _drainCandidates();
      }),
    );

    // ICE candidates (buffer until remote description is set).
    _subs.add(
      rt.callIce.listen((p) async {
        if (p.callId != _callId) return;
        final c = p.data['candidate'] as Map?;
        if (c == null || _pc == null) return;
        final candidate = RTCIceCandidate(
          '${c['candidate']}',
          c['sdpMid'] as String?,
          (c['sdpMLineIndex'] as num?)?.toInt(),
        );
        if (_remoteDescriptionSet) {
          await _pc!.addCandidate(candidate);
        } else {
          _pendingCandidates.add(candidate);
        }
      }),
    );

    // Other side ended/declined the call.
    _subs.add(
      rt.callEnded.listen((p) {
        if (p.callId == _callId) _hangUp(notify: false, state: 'Ended');
      }),
    );
  }

  Future<void> _sendOffer(ChatRealtime rt) async {
    if (_pc == null || _callId == null) return;
    final offer = await _pc!.createOffer({
      'offerToReceiveAudio': true,
      'offerToReceiveVideo': _isVideo,
    });
    await _pc!.setLocalDescription(offer);
    await rt.sendOffer(widget.employeeId, _callId!, widget.kind, {
      'sdp': offer.sdp,
      'type': offer.type,
    });
  }

  Future<void> _drainCandidates() async {
    for (final c in _pendingCandidates) {
      try {
        await _pc!.addCandidate(c);
      } catch (_) {}
    }
    _pendingCandidates.clear();
  }

  void _markConnected() {
    if (_ended) return;
    PushNotifications.instance.stopRingtone();
    // Route audio to the loudspeaker by default so it's audible.
    Helper.setSpeakerphoneOn(_speaker);
    if (mounted && _connectedAt == null) {
      setState(() {
        _status = 'Connected';
        _connectedAt = DateTime.now();
      });
    }
  }

  void _fail(String message) {
    PushNotifications.instance.stopRingtone();
    if (mounted) setState(() => _status = message);
  }

  Future<void> _hangUp({bool notify = true, String state = 'Ended'}) async {
    if (_ended) return;
    _ended = true;
    final seconds = _connectedAt == null
        ? null
        : DateTime.now().difference(_connectedAt!).inSeconds;
    if (_callId != null) {
      try {
        await ref
            .read(chatActionsProvider)
            .endCall(_callId!, state, durationSeconds: seconds);
      } catch (_) {}
    }
    await _cleanup();
    if (mounted) {
      ref.invalidate(callHistoryProvider);
      Navigator.of(context).maybePop();
    }
  }

  Future<void> _cleanup() async {
    await PushNotifications.instance.stopRingtone();
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
    await _pc?.close();
    await _localRenderer.dispose();
    await _remoteRenderer.dispose();
  }

  void _toggleMute() {
    final tracks = _localStream?.getAudioTracks() ?? [];
    for (final t in tracks) {
      t.enabled = _muted;
    }
    setState(() => _muted = !_muted);
  }

  void _toggleVideo() {
    final tracks = _localStream?.getVideoTracks() ?? [];
    for (final t in tracks) {
      t.enabled = _videoOff;
    }
    setState(() => _videoOff = !_videoOff);
  }

  void _toggleSpeaker() {
    setState(() => _speaker = !_speaker);
    Helper.setSpeakerphoneOn(_speaker);
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: Colors.black,
      body: Stack(
        children: [
          if (_isVideo)
            Positioned.fill(
              child: RTCVideoView(
                _remoteRenderer,
                objectFit: RTCVideoViewObjectFit.RTCVideoViewObjectFitCover,
              ),
            )
          else
            Center(
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  CircleAvatar(
                    radius: 56,
                    child: Text(
                      widget.name.isNotEmpty ? widget.name[0] : '?',
                      style: const TextStyle(fontSize: 40),
                    ),
                  ),
                  const SizedBox(height: 16),
                  Text(
                    widget.name,
                    style: const TextStyle(color: Colors.white, fontSize: 24),
                  ),
                ],
              ),
            ),
          if (_isVideo)
            Positioned(
              right: 16,
              top: 48,
              width: 110,
              height: 160,
              child: ClipRRect(
                borderRadius: BorderRadius.circular(10),
                child: RTCVideoView(_localRenderer, mirror: true),
              ),
            ),
          Positioned(
            top: 48,
            left: 0,
            right: 0,
            child: Column(
              children: [
                if (_isVideo)
                  Text(
                    widget.name,
                    style: const TextStyle(color: Colors.white, fontSize: 20),
                  ),
                Text(_status, style: const TextStyle(color: Colors.white70)),
              ],
            ),
          ),
          Positioned(
            bottom: 40,
            left: 0,
            right: 0,
            child: Row(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                _roundButton(
                  icon: _muted ? Icons.mic_off : Icons.mic,
                  onTap: _toggleMute,
                ),
                const SizedBox(width: 16),
                if (_isVideo)
                  _roundButton(
                    icon: _videoOff ? Icons.videocam_off : Icons.videocam,
                    onTap: _toggleVideo,
                  ),
                if (_isVideo) const SizedBox(width: 16),
                _roundButton(
                  icon: _speaker ? Icons.volume_up : Icons.volume_down,
                  onTap: _toggleSpeaker,
                ),
                const SizedBox(width: 16),
                _roundButton(
                  icon: Icons.call_end,
                  color: Colors.red,
                  onTap: () => _hangUp(state: 'Ended'),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _roundButton({
    required IconData icon,
    required VoidCallback onTap,
    Color color = Colors.white24,
  }) {
    return InkResponse(
      onTap: onTap,
      child: CircleAvatar(
        radius: 28,
        backgroundColor: color,
        child: Icon(icon, color: Colors.white),
      ),
    );
  }
}
