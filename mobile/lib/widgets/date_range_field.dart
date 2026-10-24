import 'package:flutter/material.dart';

import '../core/strings.dart';
import '../core/utils/formatters.dart';

/// Start + end date pickers in a single row (leave, travel, ranges).
/// Calls [onChanged] whenever either date changes (null while incomplete).
class DateRangeField extends StatefulWidget {
  const DateRangeField({
    super.key,
    required this.startLabel,
    required this.endLabel,
    required this.onChanged,
    this.initialStart,
    this.initialEnd,
    this.startError,
    this.endError,
    this.firstDate,
  });

  final String startLabel;
  final String endLabel;
  final DateTime? initialStart;
  final DateTime? initialEnd;
  final String? startError;
  final String? endError;

  /// Inclusive lower bound for pickers (defaults to two years back).
  final DateTime? firstDate;

  final void Function(DateTime? start, DateTime? end) onChanged;

  @override
  State<DateRangeField> createState() => _DateRangeFieldState();
}

class _DateRangeFieldState extends State<DateRangeField> {
  DateTime? _start;
  DateTime? _end;

  @override
  void initState() {
    super.initState();
    _start = widget.initialStart;
    _end = widget.initialEnd;
  }

  Future<void> _pick({
    required bool isStart,
    required DateTime initial,
    required DateTime? notBefore,
    required DateTime? notAfter,
  }) async {
    final picked = await showDatePicker(
      context: context,
      initialDate: initial,
      firstDate: widget.firstDate ?? DateTime(DateTime.now().year - 2),
      lastDate:
          notAfter ?? DateTime(DateTime.now().year + 3, DateTime.december, 31),
      currentDate: notBefore,
    );
    if (picked == null) return;
    setState(() {
      if (isStart) {
        _start = picked;
        // Keep range sane: end can't precede start.
        if (_end != null && _end!.isBefore(_start!)) _end = _start;
      } else {
        _end = picked;
      }
    });
    widget.onChanged(_start, _end);
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Expanded(
              child: _DateButton(
                label: widget.startLabel,
                value: _start,
                errorText: widget.startError,
                onTap: () => _pick(
                  isStart: true,
                  initial: _start ?? DateTime.now(),
                  notBefore: null,
                  notAfter: _end,
                ),
              ),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: _DateButton(
                label: widget.endLabel,
                value: _end,
                errorText: widget.endError,
                onTap: () => _pick(
                  isStart: false,
                  initial: _end ?? _start ?? DateTime.now(),
                  notBefore: _start,
                  notAfter: null,
                ),
              ),
            ),
          ],
        ),
        if (widget.startError == null &&
            widget.endError == null &&
            _start != null &&
            _end != null &&
            _end!.isBefore(_start!))
          Padding(
            padding: const EdgeInsets.only(top: 6, left: 4),
            child: Text(
              Strings.dateOrderError,
              style: theme.textTheme.bodySmall?.copyWith(
                color: theme.colorScheme.error,
              ),
            ),
          ),
      ],
    );
  }
}

class _DateButton extends StatelessWidget {
  const _DateButton({
    required this.label,
    required this.value,
    required this.onTap,
    this.errorText,
  });

  final String label;
  final DateTime? value;
  final String? errorText;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final hasError = errorText != null;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        InkWell(
          onTap: onTap,
          borderRadius: BorderRadius.circular(12),
          child: InputDecorator(
            decoration: InputDecoration(
              labelText: label,
              errorText: errorText,
              suffixIcon: const Icon(Icons.calendar_today_rounded, size: 18),
              errorBorder: hasError
                  ? OutlineInputBorder(
                      borderRadius: BorderRadius.circular(12),
                      borderSide: BorderSide(color: scheme.error),
                    )
                  : null,
            ),
            child: Text(
              value == null ? '—' : AppFormatters.formatDate(value!),
              style: theme.textTheme.bodyMedium?.copyWith(
                color: value == null ? scheme.onSurfaceVariant : null,
              ),
            ),
          ),
        ),
      ],
    );
  }
}
