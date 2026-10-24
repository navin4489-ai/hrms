/// Compile-time environment configuration.
///
/// The API base URL is injected at build/run time:
/// `flutter run --dart-define=API_BASE_URL=http://10.0.2.2:5080`
abstract final class Env {
  /// Base URL of the Wintech HRMS API (without `/api/v1`).
  static const String apiBaseUrl = String.fromEnvironment(
    'API_BASE_URL',
    defaultValue: 'http://localhost:5080',
  );

  /// API prefix appended to [apiBaseUrl].
  static const String apiPrefix = '/api/v1';
}
