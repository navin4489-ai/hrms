import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/network/api_exception.dart';
import '../../core/strings.dart';
import '../../core/utils/formatters.dart';
import '../../core/utils/validators.dart';
import '../../providers/attendance_provider.dart';
import '../../widgets/app_button.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/form_error.dart';

/// Submit an attendance correction (FR-EMP-035): work date, correction type,
/// proposed times and a mandatory reason. 422 field errors render inline.
class CorrectionFormScreen extends ConsumerStatefulWidget {
  const CorrectionFormScreen({super.key});

  @override
  ConsumerState<CorrectionFormScreen> createState() =>
      _CorrectionFormScreenState();
}

class _CorrectionFormScreenState extends ConsumerState<CorrectionFormScreen>
    with ServerFieldErrors<CorrectionFormScreen> {
  static const _types = <String, String>{
    'MissingPunch': 'Missing punch',
    'IncorrectAttendance': 'Incorrect attendance',
    'ShiftCorrection': 'Shift correction',
    'Overtime': 'Overtime',
  };

  final _formKey = GlobalKey<FormState>();
  final _reasonController = TextEditingController();

  DateTime? _workDate;
  String _type = 'MissingPunch';
  TimeOfDay? _checkIn;
  TimeOfDay? _checkOut;
  bool _busy = false;

  @override
  void dispose() {
    _reasonController.dispose();
    super.dispose();
  }

  static String _hhmm(TimeOfDay value) =>
      '${value.hour.toString().padLeft(2, '0')}:'
      '${value.minute.toString().padLeft(2, '0')}';

  Future<void> _pickWorkDate() async {
    final picked = await showDatePicker(
      context: context,
      initialDate: _workDate ?? DateTime.now(),
      firstDate: DateTime(DateTime.now().year - 1),
      lastDate: DateTime.now(),
    );
    if (picked == null || !mounted) return;
    setState(() {
      _workDate = picked;
      clearErrors();
    });
  }

  Future<void> _pickTime({required bool isIn}) async {
    final initial = (isIn ? _checkIn : _checkOut) ?? TimeOfDay.now();
    final picked = await showTimePicker(context: context, initialTime: initial);
    if (picked == null || !mounted) return;
    setState(() {
      if (isIn) {
        _checkIn = picked;
      } else {
        _checkOut = picked;
      }
      clearErrors();
    });
  }

  Future<void> _submit() async {
    FocusScope.of(context).unfocus();
    clearErrors();
    if (!(_formKey.currentState?.validate() ?? false)) return;

    if (_workDate == null) {
      setState(() {
        fieldErrors = const {
          'workDate': ['Please provide the work date'],
        };
      });
      return;
    }
    if (_checkIn == null && _checkOut == null) {
      setState(() {
        fieldErrors = const {
          'proposedCheckIn': ['Enter at least one proposed time'],
        };
      });
      return;
    }

    setState(() => _busy = true);
    try {
      await ref
          .read(attendanceActionsProvider)
          .submitCorrection(
            workDate: AppFormatters.toIsoDate(_workDate!),
            type: _type,
            proposedCheckIn: _checkIn == null ? null : _hhmm(_checkIn!),
            proposedCheckOut: _checkOut == null ? null : _hhmm(_checkOut!),
            reason: _reasonController.text.trim(),
          );
      ref.invalidate(correctionsProvider);
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Correction request submitted for approval'),
        ),
      );
      context.go('/attendance/corrections');
    } on ApiException catch (e) {
      captureError(e);
    } catch (_) {
      setState(() => bannerMessage = Strings.offlineMessage);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return AppScaffold(
      title: Strings.correctionRequest,
      body: SafeArea(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(16),
          child: Form(
            key: _formKey,
            autovalidateMode: AutovalidateMode.onUserInteraction,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                if (bannerMessage != null)
                  FormErrorBanner(
                    message: bannerMessage!,
                    onDismiss: clearErrors,
                  ),
                _DateField(
                  label: Strings.workDate,
                  value: _workDate,
                  errorText: errorFor('workDate'),
                  onTap: _pickWorkDate,
                ),
                const SizedBox(height: 14),
                DropdownButtonFormField<String>(
                  initialValue: _type,
                  decoration: InputDecoration(
                    labelText: Strings.correctionType,
                    prefixIcon: const Icon(Icons.tune_rounded),
                    errorText: errorFor('type'),
                  ),
                  items: [
                    for (final entry in _types.entries)
                      DropdownMenuItem(
                        value: entry.key,
                        child: Text(entry.value),
                      ),
                  ],
                  onChanged: _busy
                      ? null
                      : (value) {
                          if (value != null) {
                            setState(() => _type = value);
                            clearErrors();
                          }
                        },
                ),
                const SizedBox(height: 14),
                Row(
                  children: [
                    Expanded(
                      child: _TimeField(
                        label: Strings.proposedCheckIn,
                        value: _checkIn,
                        errorText: errorFor('proposedCheckIn'),
                        onTap: () => _pickTime(isIn: true),
                      ),
                    ),
                    const SizedBox(width: 12),
                    Expanded(
                      child: _TimeField(
                        label: Strings.proposedCheckOut,
                        value: _checkOut,
                        errorText: errorFor('proposedCheckOut'),
                        onTap: () => _pickTime(isIn: false),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 14),
                TextFormField(
                  controller: _reasonController,
                  enabled: !_busy,
                  maxLines: 3,
                  maxLength: 500,
                  validator: Validators.reason,
                  decoration: InputDecoration(
                    labelText: Strings.reasonLabel,
                    hintText: 'Describe what happened',
                    errorText: errorFor('reason'),
                  ),
                ),
                const SizedBox(height: 20),
                AppButton(
                  label: Strings.submit,
                  busy: _busy,
                  onPressed: _busy ? null : _submit,
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _DateField extends StatelessWidget {
  const _DateField({
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
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        InkWell(
          onTap: onTap,
          borderRadius: BorderRadius.circular(12),
          child: InputDecorator(
            decoration: InputDecoration(
              labelText: label,
              prefixIcon: const Icon(Icons.calendar_today_rounded, size: 18),
              suffixIcon: const Icon(Icons.arrow_drop_down_rounded),
              errorText: errorText,
            ),
            child: Text(
              value == null
                  ? Strings.selectDates
                  : AppFormatters.formatDate(value!),
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

class _TimeField extends StatelessWidget {
  const _TimeField({
    required this.label,
    required this.value,
    required this.onTap,
    this.errorText,
  });

  final String label;
  final TimeOfDay? value;
  final String? errorText;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(12),
      child: InputDecorator(
        decoration: InputDecoration(
          labelText: label,
          suffixIcon: const Icon(Icons.schedule_rounded, size: 18),
          errorText: errorText,
        ),
        child: Text(
          value == null ? '-' : _CorrectionFormScreenState._hhmm(value!),
          style: theme.textTheme.bodyMedium?.copyWith(
            color: value == null ? scheme.onSurfaceVariant : null,
          ),
        ),
      ),
    );
  }
}
