import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:image_picker/image_picker.dart';
import 'package:wintech_hrms/core/network/api_client.dart';
import 'package:wintech_hrms/core/storage/token_storage.dart';
import 'package:wintech_hrms/providers/api_provider.dart';
import 'package:wintech_hrms/providers/attendance_provider.dart';

class _Picker extends ImagePicker {
  _Picker(this.events);
  final List<String> events;
  CameraDevice? camera;

  @override
  Future<XFile?> pickImage({
    required ImageSource source,
    double? maxWidth,
    double? maxHeight,
    int? imageQuality,
    CameraDevice preferredCameraDevice = CameraDevice.rear,
    bool requestFullMetadata = true,
  }) async {
    camera = preferredCameraDevice;
    events.add('camera');
    expect(source, ImageSource.camera);
    return null; // Permission denied / user cancels: attendance must still work.
  }
}

class _Transport implements HttpClientAdapter {
  _Transport(this.events);
  final List<String> events;
  int settingsStatus = 200;
  bool photoEnabled = true;
  int punches = 0;

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    final settings = options.path.endsWith('capture-settings');
    events.add(settings ? 'settings' : 'punch');
    if (!settings) {
      expect(options.contentType, contains('multipart/form-data'));
      punches++;
    }
    return ResponseBody.fromString(
      jsonEncode(
        settings
            ? {'captureLocation': true, 'capturePhoto': photoEnabled}
            : {
                'workDate': '2026-10-26',
                'checkIn': '09:00',
                if (punches > 1) 'checkOut': '17:00',
                'message': 'Recorded',
              },
      ),
      settings ? settingsStatus : 200,
      headers: {
        Headers.contentTypeHeader: [Headers.jsonContentType],
      },
    );
  }

  @override
  void close({bool force = false}) {}
}

void main() {
  test('check-in and checkout open front camera before GPS; cancellation does not block punch', () async {
    final events = <String>[];
    final picker = _Picker(events);
    final adapter = _Transport(events);
    final api = ApiClient(
      tokenStorage: InMemoryTokenStorage(),
      httpClientAdapter: adapter,
    );
    final container = ProviderContainer(
      overrides: [
        apiClientProvider.overrideWithValue(api),
        attendanceActionsProvider.overrideWith(
          (ref) => AttendanceActions(
            ref,
            imagePicker: picker,
            locationCapture: () async {
              events.add('location');
              return null;
            },
          ),
        ),
      ],
    );
    addTearDown(container.dispose);
    addTearDown(api.close);

    final actions = container.read(attendanceActionsProvider);
    await actions.punch();
    await actions.punch();
    expect(picker.camera, CameraDevice.front);
    expect(events, [
      'settings',
      'camera',
      'location',
      'punch',
      'settings',
      'camera',
      'location',
      'punch',
    ]);
    expect(adapter.punches, 2);
  });

  test('settings outage does not prevent front camera or punch; admin photo-off is respected', () async {
    final events = <String>[];
    final picker = _Picker(events);
    final adapter = _Transport(events)..settingsStatus = 503;
    final api = ApiClient(
      tokenStorage: InMemoryTokenStorage(),
      httpClientAdapter: adapter,
    );
    final container = ProviderContainer(
      overrides: [
        apiClientProvider.overrideWithValue(api),
        attendanceActionsProvider.overrideWith(
          (ref) => AttendanceActions(
            ref,
            imagePicker: picker,
            locationCapture: () async => null,
          ),
        ),
      ],
    );
    addTearDown(container.dispose);
    addTearDown(api.close);
    await container.read(attendanceActionsProvider).punch();
    expect(events, contains('camera'));
    expect(adapter.punches, 1);

    events.clear();
    adapter.settingsStatus = 200;
    adapter.photoEnabled = false;
    await container.read(attendanceActionsProvider).punch();
    expect(events, isNot(contains('camera')));
    expect(adapter.punches, 2);
  });
}
