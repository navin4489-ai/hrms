import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/network/api_exception.dart';
import '../../core/strings.dart';
import '../../core/utils/validators.dart';
import '../../models/case_models.dart';
import '../../providers/cases_provider.dart';
import '../../widgets/app_button.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/form_error.dart';

/// Create a helpdesk case (FR-EMP-110): category (+subcategory), subject,
/// description, urgency.
class NewCaseScreen extends ConsumerStatefulWidget {
  const NewCaseScreen({super.key});

  @override
  ConsumerState<NewCaseScreen> createState() => _NewCaseScreenState();
}

class _NewCaseScreenState extends ConsumerState<NewCaseScreen>
    with ServerFieldErrors<NewCaseScreen> {
  final _formKey = GlobalKey<FormState>();
  final _subjectController = TextEditingController();
  final _descriptionController = TextEditingController();

  String? _categoryId;
  String? _subcategoryId;
  String _urgency = 'Medium';
  bool _busy = false;

  @override
  void dispose() {
    _subjectController.dispose();
    _descriptionController.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    FocusScope.of(context).unfocus();
    clearErrors();
    if (!(_formKey.currentState?.validate() ?? false)) return;
    if (_categoryId == null) {
      setState(() {
        fieldErrors = const {
          'categoryId': ['Please select a category'],
        };
      });
      return;
    }

    setState(() => _busy = true);
    try {
      await ref
          .read(caseActionsProvider)
          .createCase(
            categoryId: _categoryId!,
            subcategoryId: _subcategoryId,
            subject: _subjectController.text.trim(),
            description: _descriptionController.text.trim(),
            urgency: _urgency,
          );
      ref.invalidate(casesProvider);
      if (!mounted) return;
      ScaffoldMessenger.of(context)
          .showSnackBar(const SnackBar(content: Text(Strings.caseCreated)));
      context.go('/more/cases');
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
    final categoriesAsync = ref.watch(caseCategoriesProvider);
    final categories = categoriesAsync.value ?? const [];
    CaseCategory? selected;
    for (final category in categories) {
      if (category.id == _categoryId) {
        selected = category;
        break;
      }
    }
    final subcategories = selected?.subcategories ?? const [];

    return AppScaffold(
      title: Strings.newCase,
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
                if (categoriesAsync.isLoading)
                  const Padding(
                    padding: EdgeInsets.only(bottom: 12),
                    child: LinearProgressIndicator(),
                  ),
                DropdownButtonFormField<String>(
                  initialValue: _categoryId,
                  decoration: InputDecoration(
                    labelText: Strings.category,
                    prefixIcon: const Icon(Icons.category_outlined),
                    hintText: 'Choose a helpdesk category',
                    errorText: errorFor('categoryId'),
                  ),
                  items: [
                    for (final category in categories)
                      DropdownMenuItem(
                        value: category.id,
                        child: Text(
                          category.name,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                        ),
                      ),
                  ],
                  onChanged: _busy
                      ? null
                      : (value) {
                          setState(() {
                            _categoryId = value;
                            _subcategoryId = null;
                          });
                          clearErrors();
                        },
                ),
                if (subcategories.isNotEmpty) ...[
                  const SizedBox(height: 14),
                  DropdownButtonFormField<String>(
                    initialValue: _subcategoryId,
                    decoration: InputDecoration(
                      labelText: Strings.subcategory,
                      prefixIcon: const Icon(Icons.tune_rounded),
                      errorText: errorFor('subcategoryId'),
                    ),
                    items: [
                      const DropdownMenuItem(value: null, child: Text('None')),
                      for (final sub in subcategories)
                        DropdownMenuItem(value: sub.id, child: Text(sub.name)),
                    ],
                    onChanged: _busy
                        ? null
                        : (value) => setState(() => _subcategoryId = value),
                  ),
                ],
                const SizedBox(height: 14),
                TextFormField(
                  controller: _subjectController,
                  enabled: !_busy,
                  validator: Validators.required,
                  decoration: InputDecoration(
                    labelText: Strings.subject,
                    hintText: 'Short summary of your issue',
                    errorText: errorFor('subject'),
                  ),
                ),
                const SizedBox(height: 14),
                TextFormField(
                  controller: _descriptionController,
                  enabled: !_busy,
                  maxLines: 5,
                  minLines: 3,
                  maxLength: 2000,
                  validator: (value) => Validators.reason(value, min: 10),
                  decoration: InputDecoration(
                    labelText: Strings.description,
                    alignLabelWithHint: true,
                    hintText: 'What happened? Include any error messages.',
                    errorText: errorFor('description'),
                  ),
                ),
                const SizedBox(height: 14),
                DropdownButtonFormField<String>(
                  initialValue: _urgency,
                  decoration: InputDecoration(
                    labelText: Strings.urgency,
                    prefixIcon: const Icon(Icons.priority_high_rounded),
                    errorText: errorFor('urgency'),
                  ),
                  items: const [
                    DropdownMenuItem(value: 'Low', child: Text('Low')),
                    DropdownMenuItem(value: 'Medium', child: Text('Medium')),
                    DropdownMenuItem(value: 'High', child: Text('High')),
                    DropdownMenuItem(value: 'Urgent', child: Text('Urgent')),
                  ],
                  onChanged: _busy
                      ? null
                      : (value) {
                          if (value != null) {
                            setState(() => _urgency = value);
                            clearErrors();
                          }
                        },
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
