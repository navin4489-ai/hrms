import 'package:file_picker/file_picker.dart';
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
import '../../widgets/form_error.dart';

/// New expense claim (FR-EMP-080/081): multipart upload with optional
/// receipt; limits and duplicates are validated server-side (422 inline).
class NewExpenseScreen extends ConsumerStatefulWidget {
  const NewExpenseScreen({super.key});

  @override
  ConsumerState<NewExpenseScreen> createState() => _NewExpenseScreenState();
}

class _NewExpenseScreenState extends ConsumerState<NewExpenseScreen>
    with ServerFieldErrors<NewExpenseScreen> {
  static const _categories = <String>[
    'Travel',
    'Lodging',
    'Food',
    'Transport',
    'Office Supplies',
    'Communication',
    'Training',
    'Medical',
    'Other',
  ];

  final _formKey = GlobalKey<FormState>();
  final _amountController = TextEditingController();
  final _purposeController = TextEditingController();
  final _costCentreController = TextEditingController();
  final _categoryController = TextEditingController();

  String? _receiptPath;
  String? _receiptName;
  DateTime? _expenseDate;
  bool _busy = false;

  @override
  void initState() {
    super.initState();
    _categoryController.text = _categories.first;
  }

  @override
  void dispose() {
    _amountController.dispose();
    _purposeController.dispose();
    _costCentreController.dispose();
    _categoryController.dispose();
    super.dispose();
  }

  Future<void> _pickDate() async {
    final picked = await showDatePicker(
      context: context,
      initialDate: _expenseDate ?? DateTime.now(),
      firstDate: DateTime(DateTime.now().year - 1),
      lastDate: DateTime.now(),
    );
    if (picked == null || !mounted) return;
    setState(() {
      _expenseDate = picked;
      clearErrors();
    });
  }

  Future<void> _pickReceipt() async {
    final result = await FilePicker.pickFiles(type: FileType.any);
    if (result.isEmpty || result.first.path == null) return;
    setState(() {
      _receiptPath = result.first.path;
      _receiptName = result.first.name;
    });
  }

  Future<void> _submit() async {
    FocusScope.of(context).unfocus();
    clearErrors();
    if (!(_formKey.currentState?.validate() ?? false)) return;

    if (_expenseDate == null) {
      setState(() {
        fieldErrors = const {
          'expenseDate': ['Please provide the expense date'],
        };
      });
      return;
    }

    setState(() => _busy = true);
    try {
      await ref
          .read(expenseActionsProvider)
          .submitExpense(
            category: _categoryController.text.trim(),
            amount: double.parse(
              _amountController.text.trim().replaceAll(',', ''),
            ),
            currency: 'INR',
            expenseDate: AppFormatters.toIsoDate(_expenseDate!),
            businessPurpose: _purposeController.text.trim(),
            costCentre: _costCentreController.text.trim().isEmpty
                ? null
                : _costCentreController.text.trim(),
            receiptPath: _receiptPath,
            receiptName: _receiptName,
          );
      ref.invalidate(expensesProvider);
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Expense submitted for approval')),
      );
      context.go('/more/expenses');
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
    return AppScaffold(
      title: Strings.newExpense,
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
                Autocomplete<String>(
                  initialValue: TextEditingValue(
                    text: _categoryController.text,
                  ),
                  optionsBuilder: (valueEditingValue) {
                    final query = valueEditingValue.text.toLowerCase();
                    if (query.isEmpty) return _categories;
                    return _categories.where(
                      (option) => option.toLowerCase().contains(query),
                    );
                  },
                  onSelected: (value) {
                    _categoryController.text = value;
                    clearErrors();
                  },
                  fieldViewBuilder:
                      (context, controller, focusNode, onFieldSubmitted) {
                        return TextField(
                          controller: controller,
                          focusNode: focusNode,
                          enabled: !_busy,
                          decoration: InputDecoration(
                            labelText: Strings.expenseCategory,
                            prefixIcon: const Icon(Icons.category_outlined),
                            errorText: errorFor('category'),
                          ),
                          onChanged: (value) {
                            _categoryController.text = value;
                            clearErrors();
                          },
                          onSubmitted: (_) => onFieldSubmitted(),
                        );
                      },
                ),
                const SizedBox(height: 14),
                TextFormField(
                  controller: _amountController,
                  enabled: !_busy,
                  keyboardType: const TextInputType.numberWithOptions(
                    decimal: true,
                  ),
                  validator: Validators.amount,
                  decoration: InputDecoration(
                    labelText: '${Strings.amount} (INR)',
                    prefixIcon: const Icon(Icons.currency_rupee_rounded),
                    errorText: errorFor('amount'),
                  ),
                ),
                const SizedBox(height: 14),
                InkWell(
                  onTap: _busy ? null : _pickDate,
                  borderRadius: BorderRadius.circular(12),
                  child: InputDecorator(
                    decoration: InputDecoration(
                      labelText: Strings.expenseDate,
                      prefixIcon: const Icon(
                        Icons.calendar_today_rounded,
                        size: 18,
                      ),
                      errorText: errorFor('expenseDate'),
                    ),
                    child: Text(
                      _expenseDate == null
                          ? '-'
                          : AppFormatters.formatDate(_expenseDate!),
                      style: theme.textTheme.bodyMedium?.copyWith(
                        color: _expenseDate == null
                            ? theme.colorScheme.onSurfaceVariant
                            : null,
                      ),
                    ),
                  ),
                ),
                const SizedBox(height: 14),
                TextFormField(
                  controller: _purposeController,
                  enabled: !_busy,
                  maxLines: 2,
                  maxLength: 300,
                  validator: (value) => Validators.reason(value, min: 5),
                  decoration: InputDecoration(
                    labelText: Strings.businessPurpose,
                    hintText: 'Why was this expense incurred?',
                    errorText: errorFor('businessPurpose'),
                  ),
                ),
                const SizedBox(height: 14),
                TextFormField(
                  controller: _costCentreController,
                  enabled: !_busy,
                  decoration: InputDecoration(
                    labelText: '${Strings.costCentreLabel} (optional)',
                    prefixIcon: const Icon(Icons.business_outlined),
                    errorText: errorFor('costCentre'),
                  ),
                ),
                const SizedBox(height: 14),
                OutlinedButton.icon(
                  onPressed: _busy ? null : _pickReceipt,
                  icon: const Icon(Icons.attach_file_rounded, size: 18),
                  label: Text(
                    _receiptName ?? Strings.receiptOptional,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                  ),
                ),
                if (_receiptName != null) ...[
                  const SizedBox(height: 6),
                  Align(
                    alignment: Alignment.centerLeft,
                    child: TextButton.icon(
                      onPressed: _busy
                          ? null
                          : () => setState(() {
                              _receiptPath = null;
                              _receiptName = null;
                            }),
                      icon: const Icon(Icons.close_rounded, size: 16),
                      label: const Text('Remove receipt'),
                      style: TextButton.styleFrom(
                        padding: const EdgeInsets.symmetric(horizontal: 8),
                        minimumSize: const Size(44, 36),
                      ),
                    ),
                  ),
                ],
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
