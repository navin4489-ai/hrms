import 'package:flutter/material.dart';

import '../core/strings.dart';

/// Primary/secondary button with a built-in busy state.
class AppButton extends StatelessWidget {
  const AppButton({
    super.key,
    required this.label,
    this.onPressed,
    this.busy = false,
    this.icon,
    this.expanded = true,
    this.outlined = false,
    this.danger = false,
  });

  final String label;
  final VoidCallback? onPressed;
  final bool busy;
  final IconData? icon;
  final bool expanded;

  /// Outlined instead of filled.
  final bool outlined;

  /// Danger (destructive) styling for reject/cancel actions.
  final bool danger;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    // Busy indicator color must contrast with the button background.
    final indicatorColor = outlined
        ? scheme.primary
        : danger
        ? scheme.onError
        : scheme.onPrimary;
    final child = busy
        ? SizedBox(
            width: 20,
            height: 20,
            child: CircularProgressIndicator(
              strokeWidth: 2.4,
              color: indicatorColor,
            ),
          )
        : Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              if (icon != null) ...[
                Icon(icon, size: 18),
                const SizedBox(width: 8),
              ],
              Text(label),
            ],
          );

    final effectiveOnPressed = busy ? null : onPressed;

    Widget button;
    if (danger && !outlined) {
      button = FilledButton(
        onPressed: effectiveOnPressed,
        style: FilledButton.styleFrom(
          backgroundColor: scheme.error,
          foregroundColor: scheme.onError,
        ),
        child: child,
      );
    } else if (outlined) {
      button = OutlinedButton(
        onPressed: effectiveOnPressed,
        style: danger
            ? OutlinedButton.styleFrom(
                foregroundColor: scheme.error,
                side: BorderSide(color: scheme.error),
              )
            : null,
        child: child,
      );
    } else {
      button = FilledButton(onPressed: effectiveOnPressed, child: child);
    }

    if (!expanded) return button;
    return SizedBox(width: double.infinity, child: button);
  }
}

/// Compact tonal action used inside cards.
class MiniActionButton extends StatelessWidget {
  const MiniActionButton({
    super.key,
    required this.label,
    required this.onPressed,
    this.icon,
    this.enabled = true,
    this.destructive = false,
  });

  final String label;
  final VoidCallback? onPressed;
  final IconData? icon;
  final bool enabled;
  final bool destructive;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return TextButton.icon(
      onPressed: enabled ? onPressed : null,
      icon: icon == null ? const SizedBox.shrink() : Icon(icon, size: 17),
      label: Text(label),
      style: TextButton.styleFrom(
        foregroundColor: destructive ? scheme.error : scheme.primary,
        padding: const EdgeInsets.symmetric(horizontal: 10),
        minimumSize: const Size(44, 40),
      ),
    );
  }
}

/// Semantic wrapper reminding screens to keep touch targets accessible.
class TouchTarget extends StatelessWidget {
  const TouchTarget({super.key, required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context) =>
      SizedBox(width: 48, height: 48, child: Center(child: child));
}

/// App strings re-exported for convenience in button-heavy files.
typedef AppStrings = Strings;
