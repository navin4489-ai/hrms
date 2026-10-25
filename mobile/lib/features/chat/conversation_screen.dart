import 'dart:async';

import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:image_picker/image_picker.dart';
import 'package:record/record.dart';

import '../../core/network/api_exception.dart';
import '../../core/utils/formatters.dart';
import '../../models/chat_models.dart';
import '../../providers/chat_provider.dart';
import '../../widgets/async_body.dart';

/// Message thread for a conversation: text, image, file and voice-note sending,
/// plus audio/video call launch. Updates in real time via SignalR.
class ConversationScreen extends ConsumerStatefulWidget {
  const ConversationScreen({
    super.key,
    required this.conversationId,
    required this.title,
    this.otherEmployeeId,
    this.isGroup = false,
  });

  final String conversationId;
  final String title;
  final String? otherEmployeeId;
  final bool isGroup;

  @override
  ConsumerState<ConversationScreen> createState() => _ConversationScreenState();
}

class _ConversationScreenState extends ConsumerState<ConversationScreen> {
  final _input = TextEditingController();
  final _scroll = ScrollController();
  final _recorder = AudioRecorder();
  final List<StreamSubscription<dynamic>> _subs = [];
  bool _sending = false;
  bool _recording = false;
  DateTime? _recordStart;

  @override
  void initState() {
    super.initState();
    ref.read(chatActionsProvider).markRead(widget.conversationId);
    final rt = ref.read(chatRealtimeProvider);
    rt.connect();
    _subs.add(
      rt.messages.listen((m) {
        if (m['conversationId']?.toString() == widget.conversationId &&
            mounted) {
          ref.invalidate(conversationDetailProvider(widget.conversationId));
          ref.read(chatActionsProvider).markRead(widget.conversationId);
        }
      }),
    );
  }

  @override
  void dispose() {
    for (final s in _subs) {
      s.cancel();
    }
    _input.dispose();
    _scroll.dispose();
    _recorder.dispose();
    super.dispose();
  }

  void _afterSend() {
    ref.invalidate(conversationDetailProvider(widget.conversationId));
    ref.invalidate(conversationsProvider);
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (_scroll.hasClients) {
        _scroll.animateTo(
          _scroll.position.maxScrollExtent,
          duration: const Duration(milliseconds: 200),
          curve: Curves.easeOut,
        );
      }
    });
  }

  Future<void> _sendText() async {
    final body = _input.text.trim();
    if (body.isEmpty || _sending) return;
    setState(() => _sending = true);
    try {
      await ref.read(chatActionsProvider).sendText(widget.conversationId, body);
      _input.clear();
      _afterSend();
    } on ApiException catch (e) {
      _snack(e.message);
    } finally {
      if (mounted) setState(() => _sending = false);
    }
  }

  Future<void> _pickImage() async {
    final file = await ImagePicker().pickImage(
      source: ImageSource.gallery,
      maxWidth: 1600,
      imageQuality: 80,
    );
    if (file == null) return;
    await _sendAttachment(file.path, 'image');
  }

  Future<void> _pickFile() async {
    final result = await FilePicker.pickFiles();
    final path = result?.isNotEmpty == true ? result!.first.path : null;
    if (path == null) return;
    await _sendAttachment(path, 'file');
  }

  Future<void> _sendAttachment(
    String path,
    String kind, {
    int? duration,
  }) async {
    setState(() => _sending = true);
    try {
      await ref
          .read(chatActionsProvider)
          .sendAttachment(
            widget.conversationId,
            filePath: path,
            kind: kind,
            durationSeconds: duration,
          );
      _afterSend();
    } on ApiException catch (e) {
      _snack(e.message);
    } finally {
      if (mounted) setState(() => _sending = false);
    }
  }

  Future<void> _toggleRecord() async {
    if (_recording) {
      final path = await _recorder.stop();
      final seconds = _recordStart == null
          ? 0
          : DateTime.now().difference(_recordStart!).inSeconds;
      setState(() => _recording = false);
      if (path != null) {
        await _sendAttachment(path, 'voice', duration: seconds);
      }
    } else {
      if (!await _recorder.hasPermission()) {
        _snack('Microphone permission denied.');
        return;
      }
      final dir = await _recorder.isEncoderSupported(AudioEncoder.aacLc);
      if (!dir) {
        _snack('Voice recording is not supported on this device.');
        return;
      }
      final tmp = '${DateTime.now().millisecondsSinceEpoch}_voice.m4a';
      await _recorder.start(
        const RecordConfig(encoder: AudioEncoder.aacLc),
        path: tmp,
      );
      setState(() {
        _recording = true;
        _recordStart = DateTime.now();
      });
    }
  }

  void _snack(String msg) {
    if (mounted) {
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(msg)));
    }
  }

  void _startCall(String kind) {
    if (widget.otherEmployeeId == null) {
      _snack('Calling is available in direct chats only.');
      return;
    }
    context.push(
      '/chat/call',
      extra: {
        'employeeId': widget.otherEmployeeId,
        'name': widget.title,
        'kind': kind,
        'outgoing': true,
      },
    );
  }

  @override
  Widget build(BuildContext context) {
    final detail = ref.watch(conversationDetailProvider(widget.conversationId));
    return Scaffold(
      appBar: AppBar(
        title: Text(widget.title),
        actions: [
          if (!widget.isGroup) ...[
            IconButton(
              tooltip: 'Audio call',
              icon: const Icon(Icons.call),
              onPressed: () => _startCall('Audio'),
            ),
            IconButton(
              tooltip: 'Video call',
              icon: const Icon(Icons.videocam),
              onPressed: () => _startCall('Video'),
            ),
          ],
        ],
      ),
      body: Column(
        children: [
          Expanded(
            child: AsyncBody(
              value: detail,
              onRetry: () => ref.invalidate(
                conversationDetailProvider(widget.conversationId),
              ),
              builder: (d) {
                WidgetsBinding.instance.addPostFrameCallback((_) {
                  if (_scroll.hasClients) {
                    _scroll.jumpTo(_scroll.position.maxScrollExtent);
                  }
                });
                if (d.messages.isEmpty) {
                  return const Center(child: Text('Say hello 👋'));
                }
                return ListView.builder(
                  controller: _scroll,
                  padding: const EdgeInsets.all(12),
                  itemCount: d.messages.length,
                  itemBuilder: (context, index) => _MessageBubble(
                    message: d.messages[index],
                    showSender: d.isGroup,
                  ),
                );
              },
            ),
          ),
          _composer(),
        ],
      ),
    );
  }

  Widget _composer() {
    return SafeArea(
      top: false,
      child: Padding(
        padding: const EdgeInsets.fromLTRB(8, 4, 8, 8),
        child: Row(
          children: [
            IconButton(
              icon: const Icon(Icons.attach_file),
              onPressed: _sending ? null : _pickFile,
            ),
            IconButton(
              icon: const Icon(Icons.image_outlined),
              onPressed: _sending ? null : _pickImage,
            ),
            Expanded(
              child: TextField(
                controller: _input,
                minLines: 1,
                maxLines: 4,
                textInputAction: TextInputAction.send,
                onSubmitted: (_) => _sendText(),
                decoration: InputDecoration(
                  hintText: _recording ? 'Recording…' : 'Message',
                  isDense: true,
                  border: const OutlineInputBorder(),
                ),
              ),
            ),
            IconButton(
              icon: Icon(
                _recording ? Icons.stop_circle : Icons.mic_none,
                color: _recording ? Colors.red : null,
              ),
              onPressed: _sending ? null : _toggleRecord,
            ),
            IconButton(
              icon: const Icon(Icons.send),
              onPressed: _sending ? null : _sendText,
            ),
          ],
        ),
      ),
    );
  }
}

class _MessageBubble extends StatelessWidget {
  const _MessageBubble({required this.message, this.showSender = false});

  final ChatMessage message;
  final bool showSender;

  @override
  Widget build(BuildContext context) {
    final mine = message.mine;
    final scheme = Theme.of(context).colorScheme;
    return Align(
      alignment: mine ? Alignment.centerRight : Alignment.centerLeft,
      child: Container(
        margin: const EdgeInsets.symmetric(vertical: 3),
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
        constraints: BoxConstraints(
          maxWidth: MediaQuery.sizeOf(context).width * .75,
        ),
        decoration: BoxDecoration(
          color: mine ? scheme.primary : scheme.surfaceContainerHighest,
          borderRadius: BorderRadius.circular(14),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            if (showSender && !mine)
              Text(
                message.senderName,
                style: Theme.of(context).textTheme.labelSmall?.copyWith(
                  fontWeight: FontWeight.bold,
                  color: scheme.primary,
                ),
              ),
            _content(context, mine, scheme),
            const SizedBox(height: 2),
            Text(
              AppFormatters.time(message.createdAt),
              style: TextStyle(
                fontSize: 10,
                color: mine ? Colors.white70 : scheme.onSurfaceVariant,
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _content(BuildContext context, bool mine, ColorScheme scheme) {
    final textColor = mine ? Colors.white : scheme.onSurface;
    if (message.isVoice) {
      return Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(Icons.play_circle_fill, color: textColor),
          const SizedBox(width: 6),
          Text(
            'Voice note · ${message.durationSeconds ?? 0}s',
            style: TextStyle(color: textColor),
          ),
        ],
      );
    }
    if (message.isImage) {
      return Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(Icons.photo, color: textColor),
          const SizedBox(width: 6),
          Text('Photo', style: TextStyle(color: textColor)),
        ],
      );
    }
    if (message.isFile) {
      return Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(Icons.insert_drive_file, color: textColor),
          const SizedBox(width: 6),
          Flexible(
            child: Text(
              message.attachmentFileName ?? 'File',
              style: TextStyle(color: textColor),
            ),
          ),
        ],
      );
    }
    return Text(message.body ?? '', style: TextStyle(color: textColor));
  }
}
