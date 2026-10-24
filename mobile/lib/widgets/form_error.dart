import 'package:flutter/material.dart';

import '../core/network/api_exception.dart';
import '../core/strings.dart';

/// Mixin for form screens: maps `errors` from 400/422 responses onto
/// per-field messages and exposes a banner message.
mixin ServerFieldErrors<T extends StatefulWidget> on State<T> {
  /// Server-side field errors keyed by API field name.
  Map<String, List<String>> fieldErrors = const {};

  /// Banner-level error title/detail when no field matches.
  String? bannerMessage;

  bool get hasErrors => fieldErrors.isNotEmpty || bannerMessage != null;

  /// Error text for [field], preferring the first server message.
  String? errorFor(String field) {
    final list = fieldErrors[field.toLowerCase()] ?? fieldErrors[field];
    return (list == null || list.isEmpty) ? null : list.first;
  }

  /// Consumes an [ApiException] (or any error) into state. Returns true when
  /// the error was a validation error mapped to fields.
  bool captureError(Object error) {
    if (error is ApiException) {
      final mapped = <String, List<String>>{};
      error.errors.forEach((key, value) {
        mapped[key.toLowerCase()] = value;
      });
      setState(() {
        fieldErrors = mapped;
        if (mapped.isEmpty) {
          bannerMessage = error.message;
        } else {
          bannerMessage = null;
        }
      });
      return mapped.isNotEmpty;
    }
    setState(() {
      fieldErrors = const {};
      bannerMessage = error.toString();
    });
    return false;
  }

  void clearErrors() {
    if (!hasErrors) return;
    setState(() {
      fieldErrors = const {};
      bannerMessage = null;
    });
  }
}

/// Convenience text-style error under a field that the server rejected but
/// has no dedicated form field (e.g. `startDate` on nested rules).
String? serverError(Map<String, List<String>> errors, String field) {
  final list = errors[field.toLowerCase()] ?? errors[field];
  return (list == null || list.isEmpty) ? null : list.first;
}

/// Generic banner shown above forms when `bannerMessage` is set.
class FormErrorBanner extends StatelessWidget {
  const FormErrorBanner({super.key, required this.message, this.onDismiss});

  final String message;
  final VoidCallback? onDismiss;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Container(
      width: double.infinity,
      margin: const EdgeInsets.only(bottom: 12),
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: scheme.errorContainer,
        borderRadius: BorderRadius.circular(12),
      ),
      child: Row(
        children: [
          Icon(
            Icons.error_outline_rounded,
            color: scheme.onErrorContainer,
            size: 20,
          ),
          const SizedBox(width: 10),
          Expanded(
            child: Text(
              message,
              style: TextStyle(color: scheme.onErrorContainer, fontSize: 13.5),
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
