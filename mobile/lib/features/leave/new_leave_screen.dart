import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/network/api_exception.dart';
import '../../core/strings.dart';
import '../../core/utils/formatters.dart';
import '../../core/utils/validators.dart';
import '../../providers/dashboard_provider.dart';
import '../../providers/leave_provider.dart';
import '../../widgets/app_button.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/date_range_field.dart';
import '../../widgets/form_error.dart';

/// New leave request (FR-EMP-041/042) with live day estimate and inline
/// server 422 field errors (overlap, balance, notice rules…).
class NewLeaveScreen extends ConsumerStatefulWidget {
  const NewLeaveScreen({super.key});

  @override
  ConsumerState<NewLeaveScreen> createState() => _NewLeaveScreenState();
}

class _NewLeaveScreenState extends ConsumerState<NewLeaveScreen>
    with ServerFieldErrors<NewLeaveScreen> {
  final _formKey = GlobalKey<FormState>();
  final _reasonController = TextEditingController();
  final _attachmentController = TextEditingController();

  String? _leaveTypeId;
  String _dayType = 'Full';
  DateTime? _start;
  DateTime? _end;
  bool _busy = false;

  @override
  void dispose() {
    _reasonController.dispose();
    _attachmentController.dispose();
    super.dispose();
  }

  int get _estimatedDays {
    if (_start == null || _end == null) return 0;
    if (_dayType != 'Full') return 1;
    return AppFormatters.weekdaysBetween(_start!, _end!);
  }

  Future<void> _submit() async {
    FocusScope.of(context).unfocus();
    clearErrors();
    if (!(_formKey.currentState?.validate() ?? false)) return;

    if (_leaveTypeId == null) {
      setState(() {
        fieldErrors = const {
          'leaveTypeId': [Strings.selectLeaveType],
        };
      });
      return;
    }
    if (_start == null || _end == null) {
      setState(() {
        fieldErrors = const {
          'startDate': [Strings.selectDates],
        };
      });
      return;
    }

    setState(() => _busy = true);
    try {
      await ref
          .read(leaveActionsProvider)
          .submitRequest(
            leaveTypeId: _leaveTypeId!,
            startDate: AppFormatters.toIsoDate(_start!),
            endDate: AppFormatters.toIsoDate(_end!),
            dayType: _dayType,
            reason: _reasonController.text.trim(),
            attachmentUrl: _attachmentController.text.trim().isEmpty
                ? null
                : _attachmentController.text.trim(),
          );
      ref.invalidate(leaveRequestsProvider);
      ref.invalidate(leaveBalancesProvider);
      ref.invalidate(dashboardProvider);
      if (!mounted) return;
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(const SnackBar(content: Text('Leave request submitted')));
      context.go('/leave');
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
    final theme = Theme.of(context);
    final typesAsync = ref.watch(leaveTypesProvider);
    final types = typesAsync.value ?? const [];

    return AppScaffold(
      title: Strings.newLeaveRequest,
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
                DropdownButtonFormField<String>(
                  initialValue: _leaveTypeId,
                  decoration: InputDecoration(
                    labelText: Strings.leaveType,
                    prefixIcon: const Icon(Icons.beach_access_outlined),
                    hintText: typesAsync.isLoading
                        ? Strings.loading
                        : Strings.selectLeaveType,
                    errorText: errorFor('leaveTypeId') ?? errorFor('leaveType'),
                  ),
                  items: [
                    for (final type in types)
                      DropdownMenuItem(
                        value: type.id,
                        child: Text(
                          type.requiresAttachment
                              ? '${type.name} *'
                              : type.name,
                        ),
                      ),
                  ],
                  onChanged: _busy
                      ? null
                      : (value) {
                          setState(() => _leaveTypeId = value);
                          clearErrors();
                        },
                ),
                const SizedBox(height: 14),
                DateRangeField(
                  startLabel: Strings.startDate,
                  endLabel: Strings.endDate,
                  initialStart: _start,
                  initialEnd: _end,
                  startError: errorFor('startDate'),
                  endError: errorFor('endDate'),
                  onChanged: (start, end) {
                    setState(() {
                      _start = start;
                      _end = end;
                    });
                    clearErrors();
                  },
                ),
                const SizedBox(height: 14),
                DropdownButtonFormField<String>(
                  initialValue: _dayType,
                  decoration: InputDecoration(
                    labelText: Strings.dayType,
                    prefixIcon: const Icon(Icons.schedule_rounded),
                    errorText: errorFor('dayType'),
                  ),
                  items: const [
                    DropdownMenuItem(value: 'Full', child: Text('Full day')),
                    DropdownMenuItem(
                      value: 'FirstHalf',
                      child: Text('First half'),
                    ),
                    DropdownMenuItem(
                      value: 'SecondHalf',
                      child: Text('Second half'),
                    ),
                  ],
                  onChanged: _busy
                      ? null
                      : (value) {
                          if (value != null) {
                            setState(() => _dayType = value);
                            clearErrors();
                          }
                        },
                ),
                if (_start != null && _end != null) ...[
                  const SizedBox(height: 10),
                  Container(
                    padding: const EdgeInsets.all(10),
                    decoration: BoxDecoration(
                      color: theme.colorScheme.primaryContainer.withValues(
                        alpha: 0.5,
                      ),
                      borderRadius: BorderRadius.circular(10),
                    ),
                    child: Text(
                      '${Strings.daysEstimated}: $_estimatedDays',
                      style: theme.textTheme.bodyMedium?.copyWith(
                        fontWeight: FontWeight.w600,
                        color: theme.colorScheme.onPrimaryContainer,
                      ),
                    ),
                  ),
                ],
                const SizedBox(height: 14),
                TextFormField(
                  controller: _reasonController,
                  enabled: !_busy,
                  maxLines: 3,
                  maxLength: 500,
                  validator: Validators.reason,
                  decoration: InputDecoration(
                    labelText: Strings.reason,
                    hintText: 'Why do you need this leave?',
                    errorText: errorFor('reason'),
                  ),
                ),
                const SizedBox(height: 14),
                TextFormField(
                  controller: _attachmentController,
                  enabled: !_busy,
                  keyboardType: TextInputType.url,
                  decoration: InputDecoration(
                    labelText: Strings.attachmentUrl,
                    hintText: 'https://… (optional)',
                    prefixIcon: const Icon(Icons.link_rounded),
                    errorText: errorFor('attachmentUrl'),
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
