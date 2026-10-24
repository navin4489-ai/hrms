import 'common.dart';

/// `GET /assets` (mine) item.
class Asset {
  const Asset({
    required this.id,
    required this.assetType,
    required this.name,
    this.assetTag,
    this.serialNumber,
    this.model,
    required this.status,
    this.issuedAt,
    this.condition,
    this.accessories,
    this.acknowledgedAt,
    this.returnDueDate,
  });

  factory Asset.fromJson(Map<String, dynamic> json) => Asset(
    id: JsonX.strOr(json['id']),
    assetType: JsonX.strOr(json['assetType']),
    name: JsonX.strOr(json['name']),
    assetTag: JsonX.str(json['assetTag']),
    serialNumber: JsonX.str(json['serialNumber']),
    model: JsonX.str(json['model']),
    status: JsonX.strOr(json['status'], 'Assigned'),
    issuedAt: JsonX.str(json['issuedAt']),
    condition: JsonX.str(json['condition']),
    accessories: JsonX.str(json['accessories']),
    acknowledgedAt: JsonX.str(json['acknowledgedAt']),
    returnDueDate: JsonX.str(json['returnDueDate']),
  );

  final String id;
  final String assetType;
  final String name;
  final String? assetTag;
  final String? serialNumber;
  final String? model;
  final String status;
  final String? issuedAt;
  final String? condition;
  final String? accessories;
  final String? acknowledgedAt;
  final String? returnDueDate;

  /// FR-EMP-091: acknowledge while issued and not yet acknowledged.
  bool get canAcknowledge => acknowledgedAt == null && status == 'Assigned';

  /// FR-EMP-093: employees request a return; HR verifies the handover.
  bool get canRequestReturn => status == 'Assigned';
}
