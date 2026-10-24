import 'package:intl/intl.dart';

/// Centralized date/time/currency formatting (BR-011: display times in the
/// device's local timezone).
abstract final class AppFormatters {
  static final DateFormat _day = DateFormat('d MMM yyyy');
  static final DateFormat _dayShort = DateFormat('d MMM');
  static final DateFormat _monthYear = DateFormat('MMMM yyyy');
  static final DateFormat _time = DateFormat('HH:mm');
  static final DateFormat _dateTime = DateFormat('d MMM yyyy, HH:mm');
  static final DateFormat _isoDay = DateFormat('yyyy-MM-dd');
  static final DateFormat _weekday = DateFormat('EEE');

  /// `YYYY-MM-DD` business date -> `24 Oct 2026`. Returns null-safe input.
  static String date(String? isoDate) {
    if (isoDate == null || isoDate.isEmpty) return '—';
    final parsed = DateTime.tryParse(isoDate);
    if (parsed == null) return isoDate;
    return _day.format(parsed);
  }

  /// Short day format (`24 Oct`) used in compact lists.
  static String dateShort(String? isoDate) {
    if (isoDate == null || isoDate.isEmpty) return '—';
    final parsed = DateTime.tryParse(isoDate);
    if (parsed == null) return isoDate;
    return _dayShort.format(parsed);
  }

  /// ISO-8601 timestamp (UTC) -> local `24 Oct 2026, 09:02`.
  static String timestamp(String? iso) {
    if (iso == null || iso.isEmpty) return '—';
    final parsed = DateTime.tryParse(iso);
    if (parsed == null) return iso;
    return _dateTime.format(parsed.toLocal());
  }

  /// ISO-8601 timestamp (UTC) -> local `HH:mm`.
  static String time(String? iso) {
    if (iso == null || iso.isEmpty) return '—';
    final parsed = DateTime.tryParse(iso);
    if (parsed == null) return iso;
    return _time.format(parsed.toLocal());
  }

  /// Plain `HH:mm` business time, passed through.
  static String clockTime(String? hhmm) {
    if (hhmm == null || hhmm.isEmpty) return '—';
    return hhmm;
  }

  /// Localtime from a `yyyy-MM-dd` or ISO timestamp for calendar building.
  static DateTime? parseDate(String? iso) {
    if (iso == null || iso.isEmpty) return null;
    final parsed = DateTime.tryParse(iso);
    if (parsed == null) return null;
    return DateTime(parsed.year, parsed.month, parsed.day);
  }

  /// Localtime DateTime -> `HH:mm`.
  static String formatDateTime(DateTime value) => _time.format(value.toLocal());

  /// Localtime DateTime -> `24 Oct 2026`.
  static String formatDate(DateTime value) => _day.format(value.toLocal());

  static String monthYear(DateTime value) => _monthYear.format(value);

  static String weekdayShort(DateTime value) => _weekday.format(value);

  /// `YYYY-MM-DD` wire format from a DateTime.
  static String toIsoDate(DateTime value) {
    final local = value.toLocal();
    return _isoDay.format(DateTime(local.year, local.month, local.day));
  }

  /// Currency amount, e.g. `₹1,250.00` / `1,250.00 INR`.
  static String currency(num? amount, [String? code]) {
    if (amount == null) return '—';
    final formatted = NumberFormat('#,##0.00').format(amount);
    if (code == null || code.isEmpty) return formatted;
    return '$formatted $code';
  }

  /// Compact currency without decimals for cards.
  static String currencyCompact(num? amount, [String? code]) {
    if (amount == null) return '—';
    final formatted = NumberFormat('#,##0').format(amount);
    if (code == null || code.isEmpty) return formatted;
    return '$formatted $code';
  }

  /// Hours, e.g. `8.98 h`.
  static String hours(num? value) {
    if (value == null) return '—';
    return '${NumberFormat('0.#').format(value)} h';
  }

  /// Leave/decimal day units, e.g. `5.5`.
  static String days(num? value) {
    if (value == null) return '—';
    return NumberFormat('0.#').format(value);
  }

  /// Percent, e.g. `70%`.
  static String percent(num? value) {
    if (value == null) return '—';
    return '${value.round()}%';
  }

  /// Relative time such as `5m ago`, `2h ago`, `3d ago`.
  static String relativeTime(String? iso) {
    if (iso == null || iso.isEmpty) return '—';
    final parsed = DateTime.tryParse(iso);
    if (parsed == null) return iso;
    final diff = DateTime.now().toUtc().difference(parsed.toUtc());
    if (diff.isNegative) {
      final ahead = diff.abs();
      if (ahead.inMinutes < 60) return 'in ${ahead.inMinutes}m';
      if (ahead.inHours < 24) return 'in ${ahead.inHours}h';
      return 'in ${ahead.inDays}d';
    }
    if (diff.inMinutes < 1) return 'just now';
    if (diff.inMinutes < 60) return '${diff.inMinutes}m ago';
    if (diff.inHours < 24) return '${diff.inHours}h ago';
    if (diff.inDays < 7) return '${diff.inDays}d ago';
    return dateShort(iso.substring(0, 10));
  }

  /// Renders enum-ish server strings as friendly labels:
  /// `WaitingOnHR` -> `Waiting on HR`, `HalfDay` -> `Half Day`.
  static String enumLabel(String? value) {
    if (value == null || value.isEmpty) return '—';
    final withBreaks = value
        .replaceAllMapped(
          RegExp(r'([a-z0-9])([A-Z])'),
          (m) => '${m[1]} ${m[2]}',
        )
        .replaceAllMapped(
          RegExp(r'([A-Z]+)([A-Z][a-z])'),
          (m) => '${m[1]} ${m[2]}',
        );
    return withBreaks;
  }

  /// File size in human form, e.g. `1.2 MB`.
  static String fileSize(int? bytes) {
    if (bytes == null) return '—';
    if (bytes < 1024) return '$bytes B';
    if (bytes < 1024 * 1024) return '${(bytes / 1024).toStringAsFixed(1)} KB';
    if (bytes < 1024 * 1024 * 1024) {
      return '${(bytes / (1024 * 1024)).toStringAsFixed(1)} MB';
    }
    return '${(bytes / (1024 * 1024 * 1024)).toStringAsFixed(1)} GB';
  }

  /// `true` when the ISO date is today (device local).
  static bool isToday(String? isoDate) {
    final parsed = parseDate(isoDate);
    if (parsed == null) return false;
    final now = DateTime.now();
    return parsed.year == now.year &&
        parsed.month == now.month &&
        parsed.day == now.day;
  }

  /// Number of days between two business dates inclusive of the start.
  static int daysBetween(DateTime start, DateTime end) {
    final s = DateTime(start.year, start.month, start.day);
    final e = DateTime(end.year, end.month, end.day);
    return e.difference(s).inDays + 1;
  }

  /// Counts weekdays (Mon-Fri) in the [start, end] range — used for live
  /// leave-day hints on the request form.
  static int weekdaysBetween(DateTime start, DateTime end) {
    final s = DateTime(start.year, start.month, start.day);
    final e = DateTime(end.year, end.month, end.day);
    var count = 0;
    for (var day = s; !day.isAfter(e); day = day.add(const Duration(days: 1))) {
      final weekday = day.weekday;
      if (weekday >= DateTime.monday && weekday <= DateTime.friday) count++;
    }
    return count;
  }
}
