import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../models/me_models.dart';
import 'api_provider.dart';

/// `GET /me/dashboard` (FR-EMP-010..014).
/// Refreshed via `ref.invalidate(dashboardProvider)` (e.g. on app resume and
/// after punch actions).
final dashboardProvider = FutureProvider<DashboardData>((ref) async {
  final api = ref.watch(apiClientProvider);
  final json = await api.getMap('/me/dashboard');
  return DashboardData.fromJson(json);
});
