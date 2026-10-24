import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/network/api_exception.dart';
import '../../core/strings.dart';
import '../../core/utils/formatters.dart';
import '../../models/me_models.dart';
import '../../providers/attendance_provider.dart';
import '../../providers/auth_provider.dart';
import '../../providers/dashboard_provider.dart';
import '../../router/deep_link_parser.dart';
import '../../theme/app_theme.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';
import '../../widgets/section_card.dart';
import '../../widgets/shimmer_box.dart';
import '../../widgets/status_chip.dart';
import '../../widgets/wintech_logo.dart';

/// Employee home (FR-EMP-010..014): greeting, punch card, quick actions,
/// pending approvals, leave balances, my requests, announcements, holidays.
class DashboardScreen extends ConsumerStatefulWidget {
  const DashboardScreen({super.key});

  @override
  ConsumerState<DashboardScreen> createState() => _DashboardScreenState();
}

class _DashboardScreenState extends ConsumerState<DashboardScreen> {
  bool _punching = false;

  Future<void> _punch(DashboardAttendance attendance) async {
    final isPunchIn = attendance.canPunchIn;
    setState(() => _punching = true);
    try {
      final result = await ref.read(attendanceActionsProvider).punch();
      ref.invalidate(dashboardProvider);
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            result.message ??
                (isPunchIn
                    ? 'Punch in recorded at ${AppFormatters.clockTime(result.checkIn)}'
                    : 'Punch out recorded at ${AppFormatters.clockTime(result.checkOut)}'),
          ),
        ),
      );
    } on ApiException catch (e) {
      // 409 duplicate punch / missing window -> banner with server detail.
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text(e.detail ?? e.message)));
      }
      ref.invalidate(dashboardProvider);
    } catch (_) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(const SnackBar(content: Text(Strings.offlineMessage)));
      }
    } finally {
      if (mounted) setState(() => _punching = false);
    }
  }

  void _openDeepLink(String? deepLink) {
    final route = DeepLinkParser.toAppRoute(deepLink);
    if (route != null) context.go(route);
  }

  @override
  Widget build(BuildContext context) {
    final dashboard = ref.watch(dashboardProvider);
    final auth = ref.watch(authStateProvider);

    return AppScaffold(
      title: Strings.dashboard,
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(dashboardProvider),
        child: AsyncBody(
          value: dashboard,
          onRetry: () => ref.invalidate(dashboardProvider),
          loading: const SingleChildScrollView(
            physics: AlwaysScrollableScrollPhysics(),
            padding: EdgeInsets.all(16),
            child: SkeletonList(itemCount: 4),
          ),
          builder: (data) {
            final attendance = data.attendance;
            return SingleChildScrollView(
              physics: const AlwaysScrollableScrollPhysics(),
              padding: const EdgeInsets.all(16),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  _Header(data: data, employee: auth.user?.displayName),
                  const SizedBox(height: 16),
                  if (attendance != null) ...[
                    _AttendanceCard(
                      attendance: attendance,
                      busy: _punching,
                      canPunch: attendance.canPunch,
                      onPunch: () => _punch(attendance),
                    ),
                    const SizedBox(height: 16),
                  ],
                  if (data.quickActions.isNotEmpty) ...[
                    SectionCard(
                      title: Strings.quickActions,
                      child: _QuickActions(
                        codes: data.quickActions,
                        onSelect: _openDeepLink,
                      ),
                    ),
                    const SizedBox(height: 16),
                  ],
                  if (data.pendingApprovals > 0) ...[
                    _ApprovalsCard(count: data.pendingApprovals),
                    const SizedBox(height: 16),
                  ],
                  if (data.leaveBalances.isNotEmpty) ...[
                    SectionCard(
                      title: Strings.leaveBalances,
                      trailing: TextButton(
                        onPressed: () => context.go('/leave'),
                        child: const Text(Strings.viewAll),
                      ),
                      child: _BalanceRow(balances: data.leaveBalances),
                    ),
                    const SizedBox(height: 16),
                  ],
                  if (data.myRequests.isNotEmpty) ...[
                    SectionCard(
                      title: Strings.myRequests,
                      child: Column(
                        children: [
                          for (final item in data.myRequests.take(5))
                            _RequestTile(
                              item: item,
                              onTap: () => _openDeepLink(item.deepLink),
                            ),
                        ],
                      ),
                    ),
                    const SizedBox(height: 16),
                  ],
                  if (data.announcements.isNotEmpty) ...[
                    SectionCard(
                      title: Strings.announcements,
                      trailing: TextButton(
                        onPressed: () => context.go('/home/announcements'),
                        child: const Text(Strings.viewAll),
                      ),
                      child: Column(
                        children: [
                          for (final item in data.announcements.take(4))
                            ListTile(
                              contentPadding: EdgeInsets.zero,
                              dense: true,
                              leading: Icon(
                                item.requiresAcknowledgement
                                    ? Icons.notification_important_outlined
                                    : Icons.campaign_outlined,
                                color: Theme.of(context).colorScheme.primary,
                              ),
                              title: Text(item.title),
                              subtitle: Text(
                                [
                                  if (item.category != null &&
                                      item.category!.isNotEmpty)
                                    item.category!,
                                  AppFormatters.relativeTime(item.publishedAt),
                                ].join(' · '),
                                style: Theme.of(context).textTheme.bodySmall
                                    ?.copyWith(
                                      color: Theme.of(context)
                                          .colorScheme
                                          .onSurfaceVariant,
                                    ),
                              ),
                              trailing: const Icon(Icons.chevron_right_rounded),
                              onTap: () =>
                                  context.go('/home/announcements/${item.id}'),
                            ),
                        ],
                      ),
                    ),
                    const SizedBox(height: 16),
                  ],
                  if (data.holidays.isNotEmpty)
                    SectionCard(
                      title: Strings.upcomingHolidays,
                      child: Column(
                        children: [
                          for (final holiday in data.holidays)
                            InfoRow(
                              label: AppFormatters.date(holiday.date),
                              value: holiday.name,
                              icon: Icons.celebration_outlined,
                            ),
                        ],
                      ),
                    ),
                ],
              ),
            );
          },
        ),
      ),
    );
  }
}

class _Header extends StatelessWidget {
  const _Header({required this.data, this.employee});

  final DashboardData data;
  final String? employee;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final subtitle = [
      if (data.employee?.designation?.isNotEmpty ?? false)
        data.employee!.designation!,
      if (data.employee?.department?.isNotEmpty ?? false)
        data.employee!.department!,
    ].join(' · ');

    return ClipRRect(
      borderRadius: BorderRadius.circular(24),
      child: Container(
        decoration: const BoxDecoration(
          gradient: LinearGradient(
            begin: Alignment.topLeft,
            end: Alignment.bottomRight,
            colors: [AppColors.brandDark, AppColors.brand],
          ),
        ),
        child: Stack(
          children: [
            Positioned(
              right: -36,
              top: -54,
              child: Container(
                width: 170,
                height: 170,
                decoration: BoxDecoration(
                  shape: BoxShape.circle,
                  color: Colors.white.withValues(alpha: 0.045),
                ),
              ),
            ),
            Padding(
              padding: const EdgeInsets.all(20),
              child: Row(
                children: [
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Text(
                          data.greeting.isEmpty
                              ? 'WELCOME BACK'
                              : data.greeting.toUpperCase(),
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: theme.textTheme.labelMedium?.copyWith(
                            color: AppColors.accent,
                            fontWeight: FontWeight.w800,
                            letterSpacing: 1.1,
                          ),
                        ),
                        const SizedBox(height: 7),
                        Text(
                          employee ?? 'Your workday, together.',
                          maxLines: 2,
                          overflow: TextOverflow.ellipsis,
                          style: theme.textTheme.headlineSmall?.copyWith(
                            color: Colors.white,
                            fontWeight: FontWeight.w800,
                            letterSpacing: -0.4,
                            height: 1.05,
                          ),
                        ),
                        const SizedBox(height: 9),
                        Text(
                          AppFormatters.date(data.today),
                          style: theme.textTheme.bodyMedium?.copyWith(
                            color: Colors.white.withValues(alpha: 0.82),
                            fontWeight: FontWeight.w500,
                          ),
                        ),
                        if (subtitle.isNotEmpty) ...[
                          const SizedBox(height: 4),
                          Text(
                            subtitle,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: theme.textTheme.bodySmall?.copyWith(
                              color: Colors.white.withValues(alpha: 0.7),
                            ),
                          ),
                        ],
                      ],
                    ),
                  ),
                  const SizedBox(width: 14),
                  const WintechLogo(size: 54),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _AttendanceCard extends StatelessWidget {
  const _AttendanceCard({
    required this.attendance,
    required this.busy,
    required this.canPunch,
    required this.onPunch,
  });

  final DashboardAttendance attendance;
  final bool busy;
  final bool canPunch;
  final VoidCallback onPunch;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final shift = attendance.shift;

    return SectionCard(
      title: Strings.todayAttendance,
      trailing: StatusChip(status: attendance.status, dense: true),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Wrap(
            spacing: 20,
            runSpacing: 8,
            children: [
              _Stat(
                label: Strings.checkInLabel,
                value: AppFormatters.clockTime(attendance.checkIn),
              ),
              _Stat(
                label: Strings.checkOutLabel,
                value: AppFormatters.clockTime(attendance.checkOut),
              ),
              _Stat(
                label: Strings.workedHours,
                value: AppFormatters.hours(attendance.workedHours),
              ),
              if (shift != null)
                _Stat(
                  label: Strings.shiftLabel,
                  value:
                      '${shift.name} ${AppFormatters.clockTime(shift.startTime)}–${AppFormatters.clockTime(shift.endTime)}',
                ),
            ],
          ),
          if (attendance.status == 'NotMarked' &&
              attendance.checkIn == null &&
              attendance.checkOut == null) ...[
            const SizedBox(height: 6),
            Text(
              Strings.notMarked,
              style: theme.textTheme.bodySmall?.copyWith(
                color: scheme.onSurfaceVariant,
              ),
            ),
          ],
          if (attendance.lateByMinutes != null &&
              attendance.lateByMinutes! > 0) ...[
            const SizedBox(height: 6),
            Text(
              'Late by ${attendance.lateByMinutes} min',
              style: theme.textTheme.bodySmall?.copyWith(color: scheme.error),
            ),
          ],
          if (canPunch) ...[
            const SizedBox(height: 14),
            SizedBox(
              width: double.infinity,
              child: FilledButton.icon(
                onPressed: busy ? null : onPunch,
                icon: busy
                    ? const SizedBox(
                        width: 18,
                        height: 18,
                        child: CircularProgressIndicator(strokeWidth: 2.4),
                      )
                    : Icon(
                        attendance.canPunchIn
                            ? Icons.login_rounded
                            : Icons.logout_rounded,
                        size: 18,
                      ),
                label: Text(
                  attendance.canPunchIn ? Strings.punchIn : Strings.punchOut,
                ),
              ),
            ),
          ],
        ],
      ),
    );
  }
}

class _Stat extends StatelessWidget {
  const _Stat({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        Text(
          label,
          style: theme.textTheme.bodySmall?.copyWith(
            color: theme.colorScheme.onSurfaceVariant,
          ),
        ),
        Text(
          value,
          style: theme.textTheme.titleMedium?.copyWith(
            fontWeight: FontWeight.w700,
          ),
        ),
      ],
    );
  }
}

class _QuickActions extends StatelessWidget {
  const _QuickActions({required this.codes, required this.onSelect});

  final List<String> codes;
  final void Function(String route) onSelect;

  static const _actions = <String, (IconData, String, String)>{
    'leave.request': (Icons.beach_access_outlined, 'New leave', '/leave/new'),
    'attendance.correct': (
      Icons.edit_calendar_outlined,
      'Correct attendance',
      '/attendance/corrections/new',
    ),
    'payslip.view': (Icons.receipt_long_outlined, 'Payslips', '/more/payslips'),
    'case.create': (
      Icons.support_agent_outlined,
      'New case',
      '/more/cases/new',
    ),
    'documents.view': (
      Icons.folder_copy_outlined,
      'Documents',
      '/more/documents',
    ),
    'profile.edit': (
      Icons.person_outline_rounded,
      'Edit profile',
      '/more/profile/edit',
    ),
    'expense.create': (
      Icons.receipt_outlined,
      'New expense',
      '/more/expenses/new',
    ),
    'announcement.view': (
      Icons.campaign_outlined,
      'Announcements',
      '/home/announcements',
    ),
  };

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final entries = codes
        .map((code) => (code, _actions[code]))
        .where((entry) => entry.$2 != null)
        .toList(growable: false);
    if (entries.isEmpty) return const SizedBox.shrink();

    return Wrap(
      spacing: 12,
      runSpacing: 12,
      children: [
        for (final entry in entries)
          _QuickActionTile(
            icon: entry.$2!.$1,
            label: entry.$2!.$2,
            onTap: () => onSelect(entry.$2!.$3),
            scheme: scheme,
          ),
      ],
    );
  }
}

class _QuickActionTile extends StatelessWidget {
  const _QuickActionTile({
    required this.icon,
    required this.label,
    required this.onTap,
    required this.scheme,
  });

  final IconData icon;
  final String label;
  final VoidCallback onTap;
  final ColorScheme scheme;

  @override
  Widget build(BuildContext context) {
    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(12),
      child: Container(
        width: 104,
        padding: const EdgeInsets.symmetric(vertical: 12, horizontal: 8),
        decoration: BoxDecoration(
          color: scheme.primaryContainer.withValues(alpha: 0.5),
          borderRadius: BorderRadius.circular(12),
        ),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(icon, color: scheme.primary),
            const SizedBox(height: 8),
            Text(
              label,
              textAlign: TextAlign.center,
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
              style: Theme.of(context).textTheme.bodySmall
                  ?.copyWith(fontWeight: FontWeight.w600),
            ),
          ],
        ),
      ),
    );
  }
}

class _ApprovalsCard extends StatelessWidget {
  const _ApprovalsCard({required this.count});

  final int count;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Card(
      clipBehavior: Clip.antiAlias,
      child: ListTile(
        leading: CircleAvatar(
          backgroundColor: scheme.primaryContainer,
          foregroundColor: scheme.onPrimaryContainer,
          child: const Icon(Icons.how_to_reg_rounded),
        ),
        title: const Text(Strings.pendingApprovals),
        subtitle: Text(
          count == 1
              ? '1 request waiting for your decision'
              : '$count requests waiting for your decision',
        ),
        trailing: const Icon(Icons.chevron_right_rounded),
        onTap: () => context.go('/approvals'),
      ),
    );
  }
}

class _BalanceRow extends StatelessWidget {
  const _BalanceRow({required this.balances});

  final List<LeaveBalance> balances;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    return SizedBox(
      height: 96,
      child: ListView.separated(
        scrollDirection: Axis.horizontal,
        itemCount: balances.length,
        separatorBuilder: (_, __) => const SizedBox(width: 10),
        itemBuilder: (context, index) {
          final balance = balances[index];
          return Container(
            width: 132,
            padding: const EdgeInsets.all(12),
            decoration: BoxDecoration(
              color: scheme.surfaceContainerHighest.withValues(alpha: 0.6),
              borderRadius: BorderRadius.circular(12),
            ),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  balance.name,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: theme.textTheme.bodySmall,
                ),
                const Spacer(),
                Text(
                  AppFormatters.days(balance.available),
                  style: theme.textTheme.titleLarge?.copyWith(
                    fontWeight: FontWeight.w800,
                  ),
                ),
                Text(
                  '${balance.unit} available',
                  style: theme.textTheme.bodySmall?.copyWith(
                    color: scheme.onSurfaceVariant,
                  ),
                ),
              ],
            ),
          );
        },
      ),
    );
  }
}

class _RequestTile extends StatelessWidget {
  const _RequestTile({required this.item, required this.onTap});

  final RequestSummaryItem item;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    return ListTile(
      contentPadding: EdgeInsets.zero,
      dense: true,
      leading: CircleAvatar(
        radius: 18,
        backgroundColor: scheme.primaryContainer,
        foregroundColor: scheme.onPrimaryContainer,
        child: Icon(_iconFor(item.type), size: 18),
      ),
      title: Text(item.title, maxLines: 1, overflow: TextOverflow.ellipsis),
      subtitle: Text(
        [
          if (item.date != null && item.date!.isNotEmpty)
            AppFormatters.date(item.date),
          if (item.amount != null) AppFormatters.currency(item.amount),
        ].join(' · '),
        style: theme.textTheme.bodySmall?.copyWith(
          color: scheme.onSurfaceVariant,
        ),
      ),
      trailing: StatusChip(status: item.status, dense: true),
      onTap: item.deepLink == null ? null : onTap,
    );
  }

  static IconData _iconFor(String type) {
    switch (type) {
      case 'Leave':
        return Icons.beach_access_rounded;
      case 'Expense':
        return Icons.receipt_long_rounded;
      case 'Travel':
        return Icons.flight_rounded;
      case 'AttendanceCorrection':
        return Icons.edit_calendar_rounded;
      case 'Letter':
        return Icons.description_rounded;
      case 'ProfileChange':
        return Icons.person_rounded;
      default:
        return Icons.task_alt_rounded;
    }
  }
}
