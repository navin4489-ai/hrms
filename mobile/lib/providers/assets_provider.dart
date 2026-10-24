import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../core/network/api_client.dart';
import '../models/asset_models.dart';
import 'api_provider.dart';

/// `GET /assets` (mine) — FR-EMP-090.
final myAssetsProvider = FutureProvider<List<Asset>>((ref) async {
  final api = ref.watch(apiClientProvider);
  final list = await api.getList('/assets');
  return list
      .whereType<Map>()
      .map((e) => Asset.fromJson(Map<String, dynamic>.from(e)))
      .toList(growable: false);
});

/// Asset actions (FR-EMP-091/093).
class AssetActions {
  AssetActions(this._api);

  final ApiClient _api;

  /// `POST /assets/{id}/acknowledge` — condition is required.
  Future<void> acknowledge({
    required String id,
    required String condition,
    String? note,
  }) async {
    await _api.postMap(
      '/assets/$id/acknowledge',
      data: {
        'condition': condition,
        if (note != null && note.isNotEmpty) 'note': note,
      },
    );
  }

  /// `POST /assets/{id}/return-request` — employees cannot mark returned
  /// themselves (FR-EMP-093), only request a return.
  Future<void> requestReturn({
    required String id,
    required String reason,
    required String condition,
  }) async {
    await _api.postMap(
      '/assets/$id/return-request',
      data: {'reason': reason, 'condition': condition},
    );
  }
}

final assetActionsProvider = Provider<AssetActions>(
  (ref) => AssetActions(ref.watch(apiClientProvider)),
);
