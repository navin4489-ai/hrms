import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/network/api_exception.dart';
import '../../core/strings.dart';
import '../../core/utils/validators.dart';
import '../../providers/performance_provider.dart';
import '../../widgets/app_button.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/form_error.dart';
import '../../widgets/section_card.dart';

/// Self-assessment submission (FR-EMP-101): `POST /performance/self-assessment`
/// for the current (or passed) review cycle.
class SelfAssessmentScreen extends ConsumerStatefulWidget {
  const SelfAssessmentScreen({super.key, required this.cycleId});

  final String cycleId;

  @override
  ConsumerState<SelfAssessmentScreen> createState() =>
      _SelfAssessmentScreenState();
}

class _SelfAssessmentScreenState extends ConsumerState<SelfAssessmentScreen>
    with ServerFieldErrors<SelfAssessmentScreen> {
  final _formKey = GlobalKey<FormState>();
  final _contentController = TextEditingController();
  final _evidenceController = TextEditingController();
  bool _busy = false;

  @override
  void dispose() {
    _contentController.dispose();
    _evidenceController.dispose();
    super.dispose();
  }

  Future<void> _submit(String cycleId) async {
    FocusScope.of(context).unfocus();
    clearErrors();
    if (!(_formKey.currentState?.validate() ?? false)) return;
    setState(() => _busy = true);
    try {
      await ref
          .read(performanceActionsProvider)
          .submitSelfAssessment(
            cycleId: cycleId,
            content: _contentController.text.trim(),
            evidence: _evidenceController.text.trim().isEmpty
                ? null
                : _evidenceController.text.trim(),
          );
      ref.invalidate(currentCycleProvider);
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text(Strings.assessmentSubmitted)),
      );
      context.pop();
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
    final cycle = ref.watch(currentCycleProvider).value;
    final cycleId = widget.cycleId.isNotEmpty
        ? widget.cycleId
        : (cycle?.id ?? '');

    return AppScaffold(
      title: Strings.selfAssessment,
      body: SafeArea(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(16),
          child: cycleId.isEmpty
              ? SectionCard(
                  title: Strings.currentCycle,
                  child: Text(
                    'No review cycle is open right now. You can submit your '
                    'assessment when the cycle opens.',
                    style: theme.textTheme.bodyMedium?.copyWith(
                      color: theme.colorScheme.onSurfaceVariant,
                    ),
                  ),
                )
              : Form(
                  key: _formKey,
                  autovalidateMode: AutovalidateMode.onUserInteraction,
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      if (cycle != null)
                        SectionCard(
                          title: Strings.currentCycle,
                          dense: true,
                          child: Text(
                            '${cycle.name}'
                            '${cycle.selfAssessmentDueDate != null ? ' · due ${cycle.selfAssessmentDueDate}' : ''}',
                          ),
                        ),
                      if (bannerMessage != null) ...[
                        FormErrorBanner(
                          message: bannerMessage!,
                          onDismiss: clearErrors,
                        ),
                      ],
                      const SizedBox(height: 4),
                      TextFormField(
                        controller: _contentController,
                        enabled: !_busy,
                        maxLines: 10,
                        minLines: 6,
                        maxLength: 5000,
                        validator: (value) => Validators.required(
                          value,
                          'Please describe your achievements',
                        ),
                        decoration: InputDecoration(
                          labelText: Strings.assessmentContent,
                          alignLabelWithHint: true,
                          hintText:
                              'Key achievements, challenges, learnings and '
                              'goals for the next cycle…',
                          errorText: errorFor('content'),
                        ),
                      ),
                      const SizedBox(height: 14),
                      TextFormField(
                        controller: _evidenceController,
                        enabled: !_busy,
                        maxLines: 3,
                        decoration: InputDecoration(
                          labelText: Strings.evidence,
                          hintText:
                              'Links to documents, dashboards… (optional)',
                          prefixIcon: const Icon(Icons.link_rounded),
                          errorText: errorFor('evidence'),
                        ),
                      ),
                      const SizedBox(height: 20),
                      AppButton(
                        label: Strings.submitAssessment,
                        busy: _busy,
                        onPressed: _busy ? null : () => _submit(cycleId),
                      ),
                    ],
                  ),
                ),
        ),
      ),
    );
  }
}
