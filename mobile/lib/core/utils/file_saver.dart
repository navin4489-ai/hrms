import 'dart:io';

import 'package:path_provider/path_provider.dart';

/// Persists downloaded bytes (payslip PDFs, documents, letters) into the
/// app's documents directory and returns the absolute path.
///
/// Files never leave the device; there is no public URL involved (contract
/// §11: streamed, access-audited downloads).
Future<String> saveBytesToDevice(List<int> bytes, String fileName) async {
  final directory = await getApplicationDocumentsDirectory();
  final safeName = fileName.replaceAll(RegExp(r'[\\/:*?"<>|]'), '_');
  final file = File('${directory.path}${Platform.pathSeparator}$safeName');
  await file.writeAsBytes(bytes, flush: true);
  return file.path;
}
