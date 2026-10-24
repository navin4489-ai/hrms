import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/network/api_exception.dart';
import '../../core/strings.dart';
import '../../core/utils/formatters.dart';
import '../../core/utils/validators.dart';
import '../../providers/expenses_provider.dart';
import '../../widgets/app_button.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/date_range_field.dart';
import '../../widgets/form_error.dart';

/// New travel request (FR-EMP-083).
class NewTravelScreen extends ConsumerStatefulWidget {
  const NewTravelScreen({super.key});

  @override
  ConsumerState<NewTravelScreen> createState() => _NewTravelScreenState();
}

class _NewTravelScreenState extends ConsumerState<NewTravelScreen>
    with ServerFieldErrors<NewTravelScreen> {
  final _formKey = GlobalKey<FormState>();
  final _destinationController = TextEditingController();
  final _purposeController = TextEditingController();
  final _costController = TextEditingController();

  DateTime? _start;
  DateTime? _end;
  bool _busy = false;

  @override
  void dispose() {
    _destinationController.dispose();
    _purposeController.dispose();
    _costController.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    FocusScope.of(context).unfocus();
    clearErrors();
    if (!(_formKey.currentState?.validate() ?? false)) return;
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
          .read(expenseActionsProvider)
          .submitTravel(
            destination: _destinationController.text.trim(),
            purpose: _purposeController.text.trim(),
            startDate: AppFormatters.toIsoDate(_start!),
            endDate: AppFormatters.toIsoDate(_end!),
            estimatedCost: double.parse(
              _costController.text.trim().replaceAll(',', ''),
            ),
            currency: 'INR',
          );
      ref.invalidate(travelRequestsProvider);
      if (!mounted) return;
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(const SnackBar(content: Text('Travel request submitted')));
      context.go('/more/expenses/travel');
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
      title: Strings.newTravelRequest,
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
                TextFormField(
                  controller: _destinationController,
                  enabled: !_busy,
                  validator: Validators.required,
                  decoration: InputDecoration(
                    labelText: Strings.destination,
                    hintText: 'City or place',
                    prefixIcon: const Icon(Icons.location_on_outlined),
                    errorText: errorFor('destination'),
                  ),
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
                TextFormField(
                  controller: _purposeController,
                  enabled: !_busy,
                  maxLines: 2,
                  maxLength: 300,
                  validator: (value) => Validators.reason(value, min: 5),
                  decoration: InputDecoration(
                    labelText: Strings.purpose,
                    hintText: 'Business purpose of the trip',
                    errorText: errorFor('purpose'),
                  ),
                ),
                const SizedBox(height: 14),
                TextFormField(
                  controller: _costController,
                  enabled: !_busy,
                  keyboardType: const TextInputType.numberWithOptions(
                    decimal: true,
                  ),
                  validator: Validators.amount,
                  decoration: InputDecoration(
                    labelText: '${Strings.estimatedCost} (INR)',
                    prefixIcon: const Icon(Icons.currency_rupee_rounded),
                    errorText: errorFor('estimatedCost'),
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
