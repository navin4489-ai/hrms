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

  RTCPeerConnection? _pc;
  MediaStream? _localStream;
  String? _callId;
  String _status = 'Connecting…';
  bool _muted = false;
  bool _videoOff = false;
  bool _speaker = true;
  DateTime? _connectedAt;

  bool get _isVideo => widget.kind == 'Video';

  static const Map<String, dynamic> _config = {
    'iceServers': [
      {'urls': 'stun:stun.l.google.com:19302'},
      {'urls': 'stun:stun1.l.google.com:19302'},
    ],
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

    // Ring until the call is answered/connected.
    PushNotifications.instance.startRingtone();

    final perms = _isVideo
        ? [Permission.microphone, Permission.camera]
        : [Permission.microphone];
    await perms.request();

    final rt = ref.read(chatRealtimeProvider);
    await rt.connect();
    _bindSignaling(rt);

    try {
      _localStream = await navigator.mediaDevices.getUserMedia({
        'audio': true,
        'video': _isVideo ? {'facingMode': 'user'} : false,
      });
      _localRenderer.srcObject = _localStream;
    } catch (e) {
      setState(() => _status = 'Media error: $e');
      return;
    }

    _pc = await createPeerConnection(_config);
    for (final track in _localStream!.getTracks()) {
      await _pc!.addTrack(track, _localStream!);
    }

    _pc!.onIceCandidate = (candidate) {
      if (_callId != null) {
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
        PushNotifications.instance.stopRingtone();
        if (mounted) {
          setState(() {
            _status = 'Connected';
            _connectedAt ??= DateTime.now();
          });
        }
      }
    };
    _pc!.onConnectionState = (state) {
      if (state == RTCPeerConnectionState.RTCPeerConnectionStateDisconnected ||
          state == RTCPeerConnectionState.RTCPeerConnectionStateFailed) {
        _hangUp(notify: false, state: 'Ended');
      }
    };

    if (widget.outgoing) {
      setState(() => _status = 'Ringing…');
      _callId ??= await ref
          .read(chatActionsProvider)
          .startCall(widget.employeeId, widget.kind);
      final offer = await _pc!.createOffer();
      await _pc!.setLocalDescription(offer);
      await rt.sendOffer(widget.employeeId, _callId!, widget.kind, {
        'sdp': offer.sdp,
        'type': offer.type,
      });
    } else {
      setState(() => _status = 'Incoming…');
    }
  }

  void _bindSignaling(ChatRealtime rt) {
    _subs.add(
      rt.callOffer.listen((p) async {
        if (p.callId != _callId && _callId != null) return;
        _callId ??= p.callId;
        final sdp = p.data['sdp'] as Map?;
        if (sdp == null || _pc == null) return;
        await _pc!.setRemoteDescription(
          RTCSessionDescription('${sdp['sdp']}', '${sdp['type']}'),
        );
        final answer = await _pc!.createAnswer();
        await _pc!.setLocalDescription(answer);
        await rt.sendAnswer(widget.employeeId, _callId!, {
          'sdp': answer.sdp,
          'type': answer.type,
        });
      }),
    );
    _subs.add(
      rt.callAnswer.listen((p) async {
        if (p.callId != _callId) return;
        final sdp = p.data['sdp'] as Map?;
        if (sdp == null || _pc == null) return;
        await _pc!.setRemoteDescription(
          RTCSessionDescription('${sdp['sdp']}', '${sdp['type']}'),
        );
      }),
    );
    _subs.add(
      rt.callIce.listen((p) async {
        if (p.callId != _callId) return;
        final c = p.data['candidate'] as Map?;
        if (c == null || _pc == null) return;
        await _pc!.addCandidate(
          RTCIceCandidate(
            '${c['candidate']}',
            c['sdpMid'] as String?,
            (c['sdpMLineIndex'] as num?)?.toInt(),
          ),
        );
      }),
    );
    _subs.add(
      rt.callEnded.listen((p) {
        if (p.callId == _callId) _hangUp(notify: false, state: 'Ended');
      }),
    );
  }

  Future<void> _hangUp({bool notify = true, String state = 'Ended'}) async {
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
