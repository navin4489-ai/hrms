import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/network/api_exception.dart';
import '../../core/strings.dart';
import '../../core/utils/formatters.dart';
import '../../models/performance_models.dart';
import '../../providers/performance_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';
import '../../widgets/section_card.dart';
import '../../widgets/status_chip.dart';

/// Performance & training hub (FR-EMP-100..104): current cycle + goals,
/// published reviews, and course enrolment.
class PerformanceScreen extends ConsumerStatefulWidget {
  const PerformanceScreen({super.key});

  @override
  ConsumerState<PerformanceScreen> createState() => _PerformanceScreenState();
}

class _PerformanceScreenState extends ConsumerState<PerformanceScreen> {
  String? _acknowledgingId;
  String? _actingId;

  Future<void> _acknowledgeReview(String id) async {
    setState(() => _acknowledgingId = id);
    try {
      await ref.read(performanceActionsProvider).acknowledgeReview(id);
      ref.invalidate(reviewsProvider);
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text(Strings.reviewAcknowledged)),
        );
      }
    } on ApiException catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text(e.detail ?? e.message)));
      }
    } finally {
      if (mounted) setState(() => _acknowledgingId = null);
    }
  }

  Future<void> _enroll(String id) async {
    setState(() => _actingId = id);
    try {
      await ref.read(performanceActionsProvider).enroll(id);
      ref.invalidate(coursesProvider);
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(const SnackBar(content: Text('Enrolled in course')));
      }
    } on ApiException catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text(e.detail ?? e.message)));
      }
    } finally {
      if (mounted) setState(() => _actingId = null);
    }
  }

  Future<void> _complete(String id) async {
    setState(() => _actingId = id);
    try {
      await ref.read(performanceActionsProvider).complete(id);
      ref.invalidate(coursesProvider);
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(const SnackBar(content: Text(Strings.completed)));
      }
    } on ApiException catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text(e.detail ?? e.message)));
      }
    } finally {
      if (mounted) setState(() => _actingId = null);
    }
  }

  @override
  Widget build(BuildContext context) {
    final cycle = ref.watch(currentCycleProvider).value;
    final cycleId = cycle?.id ?? '';

    return DefaultTabController(
      length: 3,
      child: AppScaffold(
        title: Strings.performance,
        bottom: const TabBar(
          tabs: [
            Tab(text: Strings.goals),
            Tab(text: Strings.reviews),
            Tab(text: Strings.training),
          ],
        ),
        body: TabBarView(
          children: [
            // ------------------------------------------------------- goals
            RefreshIndicator(
              onRefresh: () async {
                ref.invalidate(currentCycleProvider);
                ref.invalidate(goalsProvider);
              },
              child: AsyncBody(
                value: ref.watch(goalsProvider(cycleId)),
                onRetry: () => ref.invalidate(goalsProvider(cycleId)),
                isEmpty: (items) => items.isEmpty && cycle == null,
                emptyTitle: Strings.noGoals,
                emptyMessage:
                    'Goals shared with you will appear here for your '
                    'current review cycle.',
                emptyIcon: Icons.flag_outlined,
                builder: (goals) => ListView(
                  physics: const AlwaysScrollableScrollPhysics(),
                  padding: const EdgeInsets.fromLTRB(16, 16, 16, 16),
                  children: [
                    if (cycle != null) _CycleCard(cycle: cycle),
                    if (cycle != null) const SizedBox(height: 12),
                    for (final goal in goals) ...[
                      _GoalCard(goal: goal),
                      const SizedBox(height: 10),
                    ],
                    if (goals.isEmpty && cycle != null)
                      const Padding(
                        padding: EdgeInsets.all(8),
                        child: Center(child: Text(Strings.noGoals)),
                      ),
                  ],
                ),
              ),
            ),
            // ----------------------------------------------------- reviews
            RefreshIndicator(
              onRefresh: () async => ref.invalidate(reviewsProvider),
              child: AsyncBody(
                value: ref.watch(reviewsProvider),
                onRetry: () => ref.invalidate(reviewsProvider),
                isEmpty: (items) => items.isEmpty,
                emptyTitle: Strings.noReviews,
                emptyMessage: 'Published performance reviews will appear here.',
                emptyIcon: Icons.grade_outlined,
                builder: (reviews) => ListView.separated(
                  physics: const AlwaysScrollableScrollPhysics(),
                  padding: const EdgeInsets.all(16),
                  itemCount: reviews.length,
                  separatorBuilder: (_, __) => const SizedBox(height: 10),
                  itemBuilder: (context, index) {
                    final review = reviews[index];
                    return SectionCard(
                      title: review.cycleName,
                      trailing: review.rating != null
                          ? Chip(
                              label: Text(
                                '${AppFormatters.percent(review.rating! * 20)}',
                              ),
                              visualDensity: VisualDensity.compact,
                            )
                          : null,
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          if (review.publishedAt != null)
                            InfoRow(
                              label: 'Published',
                              value: AppFormatters.date(review.publishedAt),
                            ),
                          if (review.summary != null &&
                              review.summary!.isNotEmpty)
                            Padding(
                              padding: const EdgeInsets.symmetric(vertical: 6),
                              child: Text(review.summary!),
                            ),
                          if (review.needsAcknowledgement) ...[
                            const SizedBox(height: 8),
                            SizedBox(
                              width: double.infinity,
                              child: FilledButton.tonalIcon(
                                onPressed: _acknowledgingId != null
                                    ? null
                                    : () => _acknowledgeReview(review.id),
                                icon: _acknowledgingId == review.id
                                    ? const SizedBox(
                                        width: 16,
                                        height: 16,
                                        child: CircularProgressIndicator(
                                          strokeWidth: 2,
                                        ),
                                      )
                                    : const Icon(
                                        Icons.how_to_reg_rounded,
                                        size: 18,
                                      ),
                                label: const Text(Strings.acknowledgeReview),
                              ),
                            ),
                          ] else
                            Text(
                              '${Strings.acknowledged}: '
                              '${AppFormatters.timestamp(review.acknowledgedAt)}',
                              style: Theme.of(context).textTheme.bodySmall
                                  ?.copyWith(
                                    color: Theme.of(context)
                                        .colorScheme
                                        .onSurfaceVariant,
                                  ),
                            ),
                        ],
                      ),
                    );
                  },
                ),
              ),
            ),
            // ---------------------------------------------------- training
            RefreshIndicator(
              onRefresh: () async => ref.invalidate(coursesProvider),
              child: AsyncBody(
                value: ref.watch(coursesProvider('')),
                onRetry: () => ref.invalidate(coursesProvider),
                isEmpty: (items) => items.isEmpty,
                emptyTitle: Strings.noCourses,
                emptyMessage:
                    'Assigned and optional training courses will appear here.',
                emptyIcon: Icons.school_outlined,
                builder: (courses) => ListView.separated(
                  physics: const AlwaysScrollableScrollPhysics(),
                  padding: const EdgeInsets.all(16),
                  itemCount: courses.length,
                  separatorBuilder: (_, __) => const SizedBox(height: 10),
                  itemBuilder: (context, index) {
                    final course = courses[index];
                    return SectionCard(
                      title: course.title,
                      trailing: StatusChip(
                        status: course.enrollmentStatus,
                        dense: true,
                      ),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Wrap(
                            spacing: 14,
                            runSpacing: 4,
                            children: [
                              if (course.category != null)
                                _Meta(
                                  label: Strings.category,
                                  value: course.category!,
                                ),
                              if (course.durationHours != null)
                                _Meta(
                                  label: Strings.progress,
                                  value:
                                      '${AppFormatters.hours(course.durationHours)}',
                                ),
                              if (course.dueDate != null)
                                _Meta(
                                  label: Strings.dueDate,
                                  value: AppFormatters.date(course.dueDate),
                                ),
                              if (course.mandatory)
                                _Meta(
                                  label: Strings.mandatory,
                                  value: Strings.yes,
                                ),
                            ],
                          ),
                          const SizedBox(height: 10),
                          Wrap(
                            spacing: 10,
                            children: [
                              if (course.canEnroll)
                                FilledButton.tonalIcon(
                                  onPressed: _actingId != null
                                      ? null
                                      : () => _enroll(course.id),
                                  icon: const Icon(
                                    Icons.school_rounded,
                                    size: 18,
                                  ),
                                  label: const Text(Strings.enroll),
                                ),
                              if (course.canComplete)
                                FilledButton.tonalIcon(
                                  onPressed: _actingId != null
                                      ? null
                                      : () => _complete(course.id),
                                  icon: const Icon(
                                    Icons.check_rounded,
                                    size: 18,
                                  ),
                                  label: const Text(Strings.completeCourse),
                                ),
                            ],
                          ),
                        ],
                      ),
                    );
                  },
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _Meta extends StatelessWidget {
  const _Meta({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Text(
      '$label: $value',
      style: theme.textTheme.bodySmall?.copyWith(
        color: theme.colorScheme.onSurfaceVariant,
      ),
    );
  }
}

class _CycleCard extends StatelessWidget {
  const _CycleCard({required this.cycle});

  final ReviewCycle cycle;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    return SectionCard(
      title: Strings.currentCycle,
      trailing: StatusChip(status: cycle.status ?? '-', dense: true),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            cycle.name,
            style: theme.textTheme.titleMedium?.copyWith(
              fontWeight: FontWeight.w700,
            ),
          ),
          const SizedBox(height: 8),
          InfoRow(
            label: 'Window',
            value:
                '${AppFormatters.date(cycle.startDate)} – ${AppFormatters.date(cycle.endDate)}',
          ),
          InfoRow(
            label: Strings.selfAssessment,
            value: AppFormatters.enumLabel(cycle.selfAssessmentStatus),
          ),
          if (cycle.selfAssessmentDueDate != null)
            InfoRow(
              label: Strings.dueDate,
              value: AppFormatters.date(cycle.selfAssessmentDueDate),
            ),
          const SizedBox(height: 10),
          SizedBox(
            width: double.infinity,
            child: FilledButton.icon(
              onPressed: cycle.canSubmitAssessment
                  ? () => context.go(
                      '/more/performance/self-assessment?cycleId=${cycle.id}',
                    )
                  : null,
              icon: const Icon(Icons.edit_note_rounded, size: 18),
              label: Text(
                cycle.canSubmitAssessment
                    ? Strings.selfAssessment
                    : cycle.selfAssessmentStatus == 'Submitted'
                    ? 'Assessment submitted'
                    : Strings.selfAssessment,
              ),
            ),
          ),
          const SizedBox(height: 6),
          Text(
            Strings.assessmentContent,
            style: theme.textTheme.bodySmall?.copyWith(
              color: scheme.onSurfaceVariant,
            ),
          ),
        ],
      ),
    );
  }
}

class _GoalCard extends StatelessWidget {
  const _GoalCard({required this.goal});

  final Goal goal;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    return SectionCard(
      title: goal.title,
      trailing: goal.status != null
          ? StatusChip(status: goal.status, dense: true)
          : null,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          if (goal.description != null && goal.description!.isNotEmpty)
            Padding(
              padding: const EdgeInsets.only(bottom: 8),
              child: Text(goal.description!),
            ),
          Wrap(
            spacing: 14,
            runSpacing: 4,
            children: [
              if (goal.category != null)
                _Meta(label: Strings.category, value: goal.category!),
              if (goal.weight != null)
                _Meta(
                  label: Strings.weight,
                  value: AppFormatters.percent(goal.weight),
                ),
              if (goal.target != null && goal.target!.isNotEmpty)
                _Meta(label: Strings.target, value: goal.target!),
            ],
          ),
          const SizedBox(height: 10),
          Row(
            children: [
              Expanded(
                child: ClipRRect(
                  borderRadius: BorderRadius.circular(6),
                  child: LinearProgressIndicator(
                    value: goal.progress / 100,
                    minHeight: 8,
                    backgroundColor: scheme.surfaceContainerHighest.withValues(
                      alpha: 0.7,
                    ),
                  ),
                ),
              ),
              const SizedBox(width: 10),
              Text(
                '${goal.progress}%',
                style: theme.textTheme.titleSmall?.copyWith(
                  fontWeight: FontWeight.w700,
                ),
              ),
            ],
          ),
          if (goal.managerFeedback != null &&
              goal.managerFeedback!.isNotEmpty) ...[
            const SizedBox(height: 10),
            Text(
              'Manager feedback: ${goal.managerFeedback}',
              style: theme.textTheme.bodySmall?.copyWith(
                color: scheme.onSurfaceVariant,
              ),
            ),
          ],
        ],
      ),
    );
  }
}
