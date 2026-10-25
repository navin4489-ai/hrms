import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_webrtc/flutter_webrtc.dart';

import '../../providers/call_controller.dart';

/// Global call overlay: shows the full-screen call UI, or a draggable floating
/// mini window when the user minimizes the call. Mounted once above the app so
/// the call survives navigation (back / switching tabs).
class CallOverlay extends ConsumerWidget {
  const CallOverlay({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final call = ref.watch(callControllerProvider);
    if (!call.isActive) return const SizedBox.shrink();
    return call.minimized ? const _FloatingCall() : const _FullScreenCall();
  }
}

class _FullScreenCall extends ConsumerWidget {
  const _FullScreenCall();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final c = ref.watch(callControllerProvider);
    return PopScope(
      // Back button minimizes instead of ending the call.
      canPop: false,
      onPopInvokedWithResult: (didPop, _) {
        if (!didPop) {
          ref.read(callControllerProvider).setMinimized(true);
        }
      },
      child: Material(
        color: Colors.black,
        child: Stack(
          children: [
            if (c.isVideo)
              Positioned.fill(
                child: RTCVideoView(
                  c.remoteRenderer,
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
                        c.peerName.isNotEmpty ? c.peerName[0] : '?',
                        style: const TextStyle(fontSize: 40),
                      ),
                    ),
                    const SizedBox(height: 16),
                    Text(
                      c.peerName,
                      style: const TextStyle(color: Colors.white, fontSize: 24),
                    ),
                  ],
                ),
              ),
            if (c.isVideo)
              Positioned(
                right: 16,
                top: 48,
                width: 110,
                height: 160,
                child: ClipRRect(
                  borderRadius: BorderRadius.circular(10),
                  child: RTCVideoView(c.localRenderer, mirror: true),
                ),
              ),
            // Top bar: minimize + name + status.
            Positioned(
              top: 40,
              left: 8,
              right: 8,
              child: Row(
                children: [
                  IconButton(
                    icon: const Icon(
                      Icons.fullscreen_exit,
                      color: Colors.white,
                    ),
                    tooltip: 'Minimize',
                    onPressed: () =>
                        ref.read(callControllerProvider).setMinimized(true),
                  ),
                  Expanded(
                    child: Column(
                      children: [
                        if (c.isVideo)
                          Text(
                            c.peerName,
                            style: const TextStyle(
                              color: Colors.white,
                              fontSize: 18,
                            ),
                          ),
                        Text(
                          c.statusText,
                          style: const TextStyle(color: Colors.white70),
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(width: 48),
                ],
              ),
            ),
            // Bottom controls.
            Positioned(
              bottom: 40,
              left: 0,
              right: 0,
              child: Row(
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  _btn(
                    c.muted ? Icons.mic_off : Icons.mic,
                    () => ref.read(callControllerProvider).toggleMute(),
                  ),
                  const SizedBox(width: 14),
                  if (c.isVideo) ...[
                    _btn(
                      c.videoOff ? Icons.videocam_off : Icons.videocam,
                      () => ref.read(callControllerProvider).toggleVideo(),
                    ),
                    const SizedBox(width: 14),
                  ],
                  _btn(
                    c.speaker ? Icons.volume_up : Icons.volume_down,
                    () => ref.read(callControllerProvider).toggleSpeaker(),
                  ),
                  const SizedBox(width: 14),
                  _btn(
                    Icons.call_end,
                    () => ref.read(callControllerProvider).end(),
                    color: Colors.red,
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _btn(
    IconData icon,
    VoidCallback onTap, {
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

/// Draggable floating mini-call window.
class _FloatingCall extends ConsumerStatefulWidget {
  const _FloatingCall();

  @override
  ConsumerState<_FloatingCall> createState() => _FloatingCallState();
}

class _FloatingCallState extends ConsumerState<_FloatingCall> {
  Offset _pos = const Offset(16, 80);

  @override
  Widget build(BuildContext context) {
    final c = ref.watch(callControllerProvider);
    final size = MediaQuery.sizeOf(context);
    const w = 120.0;
    const h = 170.0;
    final left = _pos.dx.clamp(0.0, size.width - w);
    final top = _pos.dy.clamp(40.0, size.height - h - 40);

    return Positioned(
      left: left,
      top: top,
      child: GestureDetector(
        onPanUpdate: (d) =>
            setState(() => _pos = Offset(left + d.delta.dx, top + d.delta.dy)),
        onTap: () => ref.read(callControllerProvider).setMinimized(false),
        child: Material(
          elevation: 8,
          borderRadius: BorderRadius.circular(12),
          color: Colors.black87,
          child: ClipRRect(
            borderRadius: BorderRadius.circular(12),
            child: SizedBox(
              width: w,
              height: h,
              child: Stack(
                fit: StackFit.expand,
                children: [
                  if (c.isVideo && c.phase == CallPhase.connected)
                    RTCVideoView(
                      c.remoteRenderer,
                      objectFit:
                          RTCVideoViewObjectFit.RTCVideoViewObjectFitCover,
                    )
                  else
                    Container(
                      color: Colors.blueGrey.shade900,
                      alignment: Alignment.center,
                      child: Icon(
                        c.isVideo ? Icons.videocam : Icons.call,
                        color: Colors.white70,
                        size: 32,
                      ),
                    ),
                  Positioned(
                    bottom: 0,
                    left: 0,
                    right: 0,
                    child: Container(
                      color: Colors.black54,
                      padding: const EdgeInsets.symmetric(
                        horizontal: 6,
                        vertical: 4,
                      ),
                      child: Row(
                        children: [
                          Expanded(
                            child: Text(
                              c.statusText == 'Connected'
                                  ? c.peerName
                                  : c.statusText,
                              style: const TextStyle(
                                color: Colors.white,
                                fontSize: 10,
                              ),
                              maxLines: 1,
                              overflow: TextOverflow.ellipsis,
                            ),
                          ),
                          GestureDetector(
                            onTap: () => ref.read(callControllerProvider).end(),
                            child: const CircleAvatar(
                              radius: 11,
                              backgroundColor: Colors.red,
                              child: Icon(
                                Icons.call_end,
                                size: 13,
                                color: Colors.white,
                              ),
                            ),
                          ),
                        ],
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
