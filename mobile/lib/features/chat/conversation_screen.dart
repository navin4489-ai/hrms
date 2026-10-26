import 'dart:async';
import 'dart:typed_data';

import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:image_picker/image_picker.dart';
import 'package:just_audio/just_audio.dart';
import 'package:open_filex/open_filex.dart';
import 'package:record/record.dart';

import '../../core/network/api_exception.dart';
import '../../core/utils/file_saver.dart';
import '../../core/utils/formatters.dart';
import '../../models/chat_models.dart';
import '../../providers/api_provider.dart';
import '../../providers/call_controller.dart';
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
    final path = result.isNotEmpty ? result.first.path : null;
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
    ref
        .read(callControllerProvider)
        .start(
          peerEmployeeId: widget.otherEmployeeId!,
          peerName: widget.title,
          kind: kind,
          outgoing: true,
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
                    key: ValueKey(d.messages[index].id),
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

class _MessageBubble extends ConsumerStatefulWidget {
  const _MessageBubble({
    super.key,
    required this.message,
    this.showSender = false,
  });

  final ChatMessage message;
  final bool showSender;

  @override
  ConsumerState<_MessageBubble> createState() => _MessageBubbleState();
}

class _MessageBubbleState extends ConsumerState<_MessageBubble> {
  bool _busy = false;
  AudioPlayer? _audio;
  Uint8List? _attachmentBytes;

  ChatMessage get message => widget.message;
  bool get showSender => widget.showSender;

  @override
  void dispose() {
    _audio?.dispose();
    super.dispose();
  }

  void _snack(String m) {
    if (mounted) {
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(m)));
    }
  }

  /// Download the attachment bytes (auth-gated) and save to a temp file.
  Future<Uint8List> _downloadBytes() async {
    if (_attachmentBytes != null) return _attachmentBytes!;
    final bytes = await ref
        .read(apiClientProvider)
        .downloadBytes('/chat/messages/${message.id}/attachment');
    return _attachmentBytes = Uint8List.fromList(bytes);
  }

  String get _fileName => (message.attachmentFileName ?? 'chat-${message.id}')
      .replaceAll(RegExp(r'[\\/:*?"<>|]'), '_');

  String _downloadError(Object error) =>
      error is ApiException && error.isNotFound
      ? 'This upload is no longer on the server. Please ask the sender to resend it.'
      : error is ApiException
      ? error.message
      : 'Download failed: $error';

  Future<String?> _downloadToFile() async {
    try {
      final bytes = await _downloadBytes();
      return await saveBytesToDevice(bytes, '${message.id}-$_fileName');
    } catch (e) {
      _snack(_downloadError(e));
      return null;
    }
  }

  Future<void> _saveAttachment() async {
    if (_busy) return;
    setState(() => _busy = true);
    try {
      final bytes = await _downloadBytes();
      final uri = await FilePicker.saveFile(
        fileName: _fileName,
        bytes: bytes,
        mimeType: message.attachmentContentType ?? 'application/octet-stream',
        dialogTitle: 'Save chat attachment',
      );
      if (uri != null) _snack('Attachment saved to your selected location.');
    } catch (error) {
      _snack(_downloadError(error));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _openFile() async {
    if (_busy) return;
    setState(() => _busy = true);
    final path = await _downloadToFile();
    if (mounted) setState(() => _busy = false);
    if (path != null && mounted) {
      final result = await OpenFilex.open(path);
      if (result.type != ResultType.done) {
        _snack('Saved to device. ${result.message}');
      }
    }
  }

  Future<void> _viewImage() async {
    if (_busy) return;
    setState(() => _busy = true);
    Uint8List? bytes;
    try {
      bytes = await _downloadBytes();
    } catch (e) {
      _snack(_downloadError(e));
    }
    if (mounted) setState(() => _busy = false);
    if (bytes != null && mounted) {
      await showDialog<void>(
        context: context,
        builder: (dialogContext) => Dialog(
          backgroundColor: Colors.black,
          insetPadding: const EdgeInsets.all(8),
          child: Stack(
            children: [
              InteractiveViewer(
                child: Center(
                  child: Image.memory(
                    bytes!,
                    errorBuilder: (_, _, _) => const Padding(
                      padding: EdgeInsets.all(24),
                      child: Text(
                        'Image preview is not supported. Use Download to save and open it.',
                        style: TextStyle(color: Colors.white),
                      ),
                    ),
                  ),
                ),
              ),
              Positioned(
                top: 4,
                right: 4,
                child: IconButton(
                  icon: const Icon(Icons.close, color: Colors.white),
                  onPressed: () => Navigator.pop(dialogContext),
                ),
              ),
              Positioned(
                bottom: 8,
                right: 8,
                child: FloatingActionButton.small(
                  onPressed: _saveAttachment,
                  child: const Icon(Icons.download),
                ),
              ),
            ],
          ),
        ),
      );
    }
  }

  Future<void> _playVoice() async {
    if (_busy) return;
    setState(() => _busy = true);
    try {
      final path = await _downloadToFile();
      if (path != null) {
        _audio ??= AudioPlayer();
        await _audio!.setFilePath(path);
        await _audio!.play();
      }
    } catch (e) {
      _snack('Playback failed: $e');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

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
    final spinner = SizedBox(
      width: 16,
      height: 16,
      child: CircularProgressIndicator(strokeWidth: 2, color: textColor),
    );

    if (message.isVoice) {
      return InkWell(
        onTap: _playVoice,
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            _busy ? spinner : Icon(Icons.play_circle_fill, color: textColor),
            const SizedBox(width: 6),
            Text(
              'Voice note · ${message.durationSeconds ?? 0}s',
              style: TextStyle(color: textColor),
            ),
          ],
        ),
      );
    }
    if (message.isImage) {
      return InkWell(
        onTap: _viewImage,
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            _busy ? spinner : Icon(Icons.image, color: textColor),
            const SizedBox(width: 6),
            Text('Photo · tap to view', style: TextStyle(color: textColor)),
          ],
        ),
      );
    }
    if (message.isFile) {
      return InkWell(
        onTap: _openFile,
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            _busy ? spinner : Icon(Icons.insert_drive_file, color: textColor),
            const SizedBox(width: 6),
            Flexible(
              child: Text(
                message.attachmentFileName ?? 'File · tap to open',
                style: TextStyle(color: textColor),
                overflow: TextOverflow.ellipsis,
              ),
            ),
            const SizedBox(width: 4),
            IconButton(
              tooltip: 'Save attachment',
              onPressed: _busy ? null : _saveAttachment,
              icon: Icon(Icons.download_rounded, size: 20, color: textColor),
            ),
          ],
        ),
      );
    }
    return Text(message.body ?? '', style: TextStyle(color: textColor));
  }
}
