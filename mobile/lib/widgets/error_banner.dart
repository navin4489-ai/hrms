import 'package:flutter/material.dart';

import '../core/network/api_exception.dart';
import '../core/strings.dart';

/// Inline error banner with optional Retry (used by [AsyncBody] and forms).
class ErrorBanner extends StatelessWidget {
  const ErrorBanner({
    super.key,
    required this.message,
    this.title,
    this.onRetry,
    this.onDismiss,
  });

  /// Creates a banner from any thrown object; knows how to unwrap
  /// [ApiException] (including validation error maps).
  factory ErrorBanner.fromError(
    Object error, {
    Key? key,
    VoidCallback? onRetry,
    VoidCallback? onDismiss,
  }) {
    if (error is ApiException) {
      return ErrorBanner(
        key: key,
        title: error.title,
        message: error.message,
        onRetry: error.statusCode == 429 ? null : onRetry,
        onDismiss: onDismiss,
      );
    }
    return ErrorBanner(
      key: key,
      message: Strings.offlineMessage,
      onRetry: onRetry,
      onDismiss: onDismiss,
    );
  }

  final String message;
  final String? title;
  final VoidCallback? onRetry;
  final VoidCallback? onDismiss;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: scheme.errorContainer.withValues(alpha: 0.6),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: scheme.error.withValues(alpha: 0.4)),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(
            Icons.error_outline_rounded,
            color: scheme.onErrorContainer,
            size: 20,
          ),
          const SizedBox(width: 10),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                if (title != null && title!.isNotEmpty)
                  Text(
                    title!,
                    style: TextStyle(
                      color: scheme.onErrorContainer,
                      fontWeight: FontWeight.w700,
                      fontSize: 13.5,
                    ),
                  ),
                Text(
                  message,
                  style: TextStyle(
                    color: scheme.onErrorContainer,
                    fontSize: 13.5,
                  ),
                ),
                if (onRetry != null) ...[
                  const SizedBox(height: 6),
                  Align(
                    alignment: Alignment.centerLeft,
                    child: TextButton.icon(
                      onPressed: onRetry,
                      icon: const Icon(Icons.refresh_rounded, size: 18),
                      label: const Text(Strings.retry),
                      style: TextButton.styleFrom(
                        foregroundColor: scheme.onErrorContainer,
                        padding: const EdgeInsets.symmetric(horizontal: 8),
                        minimumSize: const Size(48, 36),
                      ),
                    ),
                  ),
                ],
              ],
            ),
          ),
          if (onDismiss != null)
            IconButton(
              onPressed: onDismiss,
              tooltip: Strings.close,
              icon: Icon(
                Icons.close_rounded,
                size: 18,
                color: scheme.onErrorContainer,
              ),
            ),
        ],
      ),
    );
  }
}
