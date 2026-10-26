import 'package:dio/dio.dart';
import 'package:geocoding/geocoding.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:geolocator/geolocator.dart';
import 'package:image_picker/image_picker.dart';

import '../core/network/api_client.dart';
import '../models/attendance_models.dart';
import 'api_provider.dart';

/// One month of attendance including the month summary.
class MonthAttendance {
  const MonthAttendance({required this.records, required this.summary});

  final List<AttendanceRecord> records;
  final AttendanceMonthSummary summary;
}

/// `GET /attendance?month=YYYY-MM` (FR-EMP-030).
/// Family key: `YYYY-MM`.
final attendanceMonthProvider = FutureProvider.family<MonthAttendance, String>((
  ref,
  month,
) async {
  final api = ref.watch(apiClientProvider);
  final json = await api.getMap('/attendance', query: {'month': month});
  final items = (json['items'] as List? ?? const [])
      .whereType<Map>()
      .map((e) => AttendanceRecord.fromJson(Map<String, dynamic>.from(e)))
      .toList(growable: false);
  final summary = json['summary'] is Map
      ? AttendanceMonthSummary.fromJson(
          Map<String, dynamic>.from(json['summary'] as Map),
        )
      : const AttendanceMonthSummary();
  return MonthAttendance(records: items, summary: summary);
});

/// `GET /attendance/today` (FR-EMP-031 punch state on the attendance screen).
final attendanceTodayProvider = FutureProvider<AttendanceRecord>((ref) async {
  final api = ref.watch(apiClientProvider);
  final json = await api.getMap('/attendance/today');
  return AttendanceRecord.fromJson(json);
});

/// Admin-controlled capture settings the app reads before punching.
class CaptureSettings {
  const CaptureSettings({
    this.captureLocation = true,
    this.capturePhoto = true,
  });

  factory CaptureSettings.fromJson(Map<String, dynamic> json) =>
      CaptureSettings(
        captureLocation: json['captureLocation'] as bool? ?? true,
        capturePhoto: json['capturePhoto'] as bool? ?? true,
      );

  final bool captureLocation;
  final bool capturePhoto;
}

/// `GET /attendance/capture-settings` — what to collect on a punch.
final captureSettingsProvider = FutureProvider<CaptureSettings>((ref) async {
  final api = ref.watch(apiClientProvider);
  final json = await api.getMap('/attendance/capture-settings');
  return CaptureSettings.fromJson(json);
}, retry: (_, _) => null);

List<AttendanceCorrection> _correctionItems(Map<String, dynamic> json) {
  final raw = json['items'];
  final list = raw is List ? raw : const [];
  return list
      .whereType<Map>()
      .map((e) => AttendanceCorrection.fromJson(Map<String, dynamic>.from(e)))
      .toList(growable: false);
}

/// `GET /attendance-corrections?status=&month=` (FR-EMP-036).
/// Family key: status filter (`''` = all).
final correctionsProvider =
    FutureProvider.family<List<AttendanceCorrection>, String>((
      ref,
      status,
    ) async {
      final api = ref.watch(apiClientProvider);
      final query = <String, dynamic>{};
      if (status.isNotEmpty) query['status'] = status;
      final json = await api.getMap('/attendance-corrections', query: query);
      return _correctionItems(json);
    });

/// `GET /attendance-corrections/{id}` with decision history.
final correctionDetailProvider =
    FutureProvider.family<AttendanceCorrectionDetail, String>((ref, id) async {
      final api = ref.watch(apiClientProvider);
      final json = await api.getMap('/attendance-corrections/$id');
      return AttendanceCorrectionDetail.fromJson(json);
    });

/// Actions for the attendance domain.
class AttendanceActions {
  AttendanceActions(
    this._ref, {
    ImagePicker? imagePicker,
    this.locationCapture,
    this.locationNameCapture,
  }) : _imagePicker = imagePicker ?? ImagePicker();

  final Ref _ref;
  final ImagePicker _imagePicker;
  final Future<Position?> Function()? locationCapture;
  final Future<String?> Function(double latitude, double longitude)?
  locationNameCapture;
  ApiClient get _api => _ref.read(apiClientProvider);

  /// Best-effort capture of geolocation when enabled; returns null on denial/timeout.
  Future<Position?> _captureLocation() async {
    try {
      if (locationCapture != null) return await locationCapture!();
      if (!await Geolocator.isLocationServiceEnabled()) return null;
      var permission = await Geolocator.checkPermission();
      if (permission == LocationPermission.denied) {
        permission = await Geolocator.requestPermission();
      }
      if (permission == LocationPermission.denied ||
          permission == LocationPermission.deniedForever) {
        return null;
      }
      return await Geolocator.getCurrentPosition(
        locationSettings: const LocationSettings(
          accuracy: LocationAccuracy.high,
          timeLimit: Duration(seconds: 10),
        ),
      );
    } catch (_) {
      return null;
    }
  }

  /// Best-effort selfie capture when enabled; returns null if the user cancels.
  Future<XFile?> _captureSelfie() async {
    try {
      return await _imagePicker.pickImage(
        source: ImageSource.camera,
        preferredCameraDevice: CameraDevice.front,
        maxWidth: 1080,
        imageQuality: 70,
      );
    } catch (_) {
      return null;
    }
  }

  /// Reverse-geocode coordinates to a concise locality label. Best-effort and
  /// never allowed to block or fail a punch.
  Future<String?> _locationName(Position? position) async {
    if (position == null) return null;
    try {
      if (locationNameCapture != null) {
        return await locationNameCapture!(
          position.latitude,
          position.longitude,
        );
      }
      final placemarks = await placemarkFromCoordinates(
        position.latitude,
        position.longitude,
      ).timeout(const Duration(seconds: 5));
      if (placemarks.isEmpty) return null;
      final p = placemarks.first;
      final parts = <String?>[
        p.name,
        p.subLocality,
        p.locality,
        p.administrativeArea,
        p.country,
      ].map((s) => s?.trim()).whereType<String>().where((s) => s.isNotEmpty);
      final unique = <String>[];
      for (final part in parts) {
        if (!unique.any(
          (existing) => existing.toLowerCase() == part.toLowerCase(),
        )) {
          unique.add(part);
        }
      }
      if (unique.isEmpty) return null;
      final label = unique.take(3).join(', ');
      return label.length <= 300 ? label : label.substring(0, 300);
    } catch (_) {
      return null;
    }
  }

  /// `POST /attendance/punch` (multipart). Captures location + selfie per admin
  /// settings; punch still succeeds if the employee denies a permission.
  Future<PunchResult> punch() async {
    // Re-read admin settings for each punch rather than using an indefinitely
    // cached value. A transient settings failure must not block attendance.
    CaptureSettings settings;
    try {
      settings = await _ref.refresh(captureSettingsProvider.future);
    } catch (_) {
      settings = const CaptureSettings();
    }

    // Open the front camera before waiting for GPS. Both check-in and checkout
    // use this same path; an explicit admin photo-off setting is respected.
    XFile? selfie;
    if (settings.capturePhoto) selfie = await _captureSelfie();

    Position? position;
    if (settings.captureLocation) position = await _captureLocation();
    final locationName = settings.captureLocation
        ? await _locationName(position)
        : null;

    final form = FormData.fromMap({
      'source': 'Mobile',
      if (position != null) 'latitude': position.latitude,
      if (position != null) 'longitude': position.longitude,
      if (position != null) 'accuracyMeters': position.accuracy,
      if (locationName != null) 'locationName': locationName,
      if (selfie != null)
        'photo': await MultipartFile.fromFile(
          selfie.path,
          filename: selfie.name,
        ),
    });

    final json = await _api.postMultipart('/attendance/punch', formData: form);
    return PunchResult.fromJson(json);
  }

  /// `POST /attendance-corrections` -> 201.
  Future<AttendanceCorrection> submitCorrection({
    required String workDate,
    required String type,
    String? proposedCheckIn,
    String? proposedCheckOut,
    required String reason,
  }) async {
    final json = await _api.postMap(
      '/attendance-corrections',
      data: {
        'workDate': workDate,
        'type': type,
        'proposedCheckIn': proposedCheckIn,
        'proposedCheckOut': proposedCheckOut,
        'reason': reason,
        'attachmentUrl': null,
      },
    );
    return AttendanceCorrection.fromJson(json);
  }
}

final attendanceActionsProvider = Provider<AttendanceActions>(
  (ref) => AttendanceActions(ref),
);
