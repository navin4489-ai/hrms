import '../strings.dart';

/// Form validation shared across screens. Messages mirror the API's 422
/// `errors` map keys where applicable so client-side and server-side
/// validation feel identical.
abstract final class Validators {
  static String? required(
    String? value, [
    String message = Strings.fieldRequired,
  ]) {
    if (value == null || value.trim().isEmpty) return message;
    return null;
  }

  static String? notRequired(String? value) => null;

  static String? username(String? value) {
    if (value == null || value.trim().isEmpty) {
      return Strings.fieldRequired;
    }
    return null;
  }

  static String? password(String? value) {
    if (value == null || value.isEmpty) return Strings.fieldRequired;
    if (value.length < 8) return Strings.passwordTooShort;
    return null;
  }

  static String? newPassword(String? value) => password(value);

  static String? confirmPassword(String? value, String original) {
    if (value == null || value.isEmpty) return Strings.fieldRequired;
    if (value != original) return Strings.passwordMismatch;
    return null;
  }

  static String? email(String? value) {
    if (value == null || value.trim().isEmpty) return Strings.fieldRequired;
    final trimmed = value.trim();
    final ok = RegExp(r'^[^@\s]+@[^@\s]+\.[^@\s]+$').hasMatch(trimmed);
    if (!ok) return Strings.invalidEmail;
    return null;
  }

  static String? emailIfPresent(String? value) {
    if (value == null || value.trim().isEmpty) return null;
    return email(value);
  }

  static String? phoneIfPresent(String? value) {
    if (value == null || value.trim().isEmpty) return null;
    final trimmed = value.trim();
    final ok = RegExp(r'^\+?[0-9][0-9\- ()]{5,19}$').hasMatch(trimmed);
    if (!ok) return Strings.invalidPhone;
    return null;
  }

  static String? reason(String? value, {int min = 5}) {
    if (value == null || value.trim().isEmpty) return Strings.reasonRequired;
    if (value.trim().length < min) return Strings.reasonTooShort;
    return null;
  }

  /// Start/end date ordering (mirrors API 422 `startDate` errors).
  static String? dateRange(DateTime? start, DateTime? end) {
    if (start == null || end == null) return null;
    if (end.isBefore(start)) return Strings.dateOrderError;
    return null;
  }

  /// Positive numeric amount.
  static String? amount(String? value) {
    if (value == null || value.trim().isEmpty) return Strings.fieldRequired;
    final parsed = double.tryParse(value.trim().replaceAll(',', ''));
    if (parsed == null || parsed <= 0) return 'Enter a valid amount';
    return null;
  }

  /// Combines several validators, returning the first non-null error.
  static String? Function(String?) all(List<String? Function(String?)> checks) {
    return (String? value) {
      for (final check in checks) {
        final result = check(value);
        if (result != null) return result;
      }
      return null;
    };
  }
}
