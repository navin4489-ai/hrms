import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../core/strings.dart';
import 'empty_state.dart';
import 'error_banner.dart';

/// Standard loading / error / empty / data state machine for async screens.
///
/// Wrap every `ref.watch(someFutureProvider)` with this widget:
/// ```dart
/// AsyncBody(
///   value: ref.watch(provider),
///   onRetry: () => ref.invalidate(provider),
///   isEmpty: (data) => data.isEmpty,
///   emptyMessage: 'No items yet',
///   builder: (data) => ItemList(data),
/// )
/// ```
class AsyncBody<T> extends StatelessWidget {
  const AsyncBody({
    super.key,
    required this.value,
    required this.builder,
    this.onRetry,
    this.isEmpty,
    this.emptyTitle = Strings.emptyTitle,
    this.emptyMessage = Strings.emptyGeneric,
    this.emptyIcon = Icons.inbox_rounded,
    this.loading,
    this.padding = const EdgeInsets.all(16),
  });

  final AsyncValue<T> value;
  final Widget Function(T data) builder;
  final VoidCallback? onRetry;
  final bool Function(T data)? isEmpty;
  final String emptyTitle;
  final String emptyMessage;
  final IconData emptyIcon;
  final Widget? loading;
  final EdgeInsetsGeometry padding;

  @override
  Widget build(BuildContext context) {
    return value.when(
      loading: () =>
          loading ??
          const Center(
            child: Padding(
              padding: EdgeInsets.all(32),
              child: CircularProgressIndicator(),
            ),
          ),
      error: (error, stack) => ErrorBanner(
        message: error is Exception && error.toString().isNotEmpty
            ? _messageOf(error)
            : Strings.somethingWentWrong,
        onRetry: onRetry,
      ),
      data: (data) {
        if (isEmpty?.call(data) ?? false) {
          return EmptyState(
            icon: emptyIcon,
            title: emptyTitle,
            message: emptyMessage,
          );
        }
        return builder(data);
      },
    );
  }

  static String _messageOf(Object error) {
    // ApiException exposes a friendly message; other errors fall back.
    final dynamic d = error;
    try {
      final msg = d.message;
      if (msg is String && msg.isNotEmpty) return msg;
    } catch (_) {}
    return error.toString();
  }
}
